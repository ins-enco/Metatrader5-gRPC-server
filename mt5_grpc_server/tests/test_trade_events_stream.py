"""Poll-loop / streaming tests for TradeEventsServiceImpl.

All tests drive the server-streaming RPC against a **mock** ``history_deals_get``
(no live broker), asserting exactly-once/ordering, default-start-is-now, backfill
cap, cadence clamping, in-band error reporting, prompt cancellation, and resume
continuity.

Covers: SC-002, SC-003, SC-004, FR-004, FR-005, FR-006, FR-007, FR-009 and
User Stories 1, 2, 3.
"""
import sys
import os
from collections import namedtuple

import pytest

# Make the proto package and server package importable when run from repo root.
_REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
for _p in (os.path.join(_REPO_ROOT, "mt5_grpc_proto"), os.path.join(_REPO_ROOT, "mt5_grpc_server")):
    if _p not in sys.path:
        sys.path.insert(0, _p)

from mt5_grpc_server.imp import trade_events as te  # noqa: E402
from mt5_grpc_proto.trade_events_pb2 import SubscribeTradeTransactionsRequest  # noqa: E402


NOW_MS = 1_700_000_000_000  # fixed "current server time" for deterministic tests


FakeDeal = namedtuple(
    "FakeDeal",
    "ticket order position_id symbol volume price profit time_msc type entry",
)


def make_deal(ticket, time_msc, type=0, entry=0, order=None, position_id=None,
              symbol="EURUSD", volume=1.0, price=1.1, profit=0.0):
    return FakeDeal(
        ticket=ticket,
        order=order if order is not None else ticket,
        position_id=position_id if position_id is not None else ticket,
        symbol=symbol,
        volume=volume,
        price=price,
        profit=profit,
        time_msc=time_msc,
        type=type,
        entry=entry,
    )


class FakeContext:
    """Minimal gRPC servicer context stand-in.

    ``is_active()`` returns ``active`` unless ``max_active_calls`` is set, in which
    case it returns True for that many calls then False (used to simulate a client
    cancelling mid-batch).
    """

    def __init__(self, active=True, max_active_calls=None):
        self.active = active
        self.max_active_calls = max_active_calls
        self.is_active_calls = 0

    def is_active(self):
        self.is_active_calls += 1
        if self.max_active_calls is not None:
            return self.is_active_calls <= self.max_active_calls
        return self.active


class Harness:
    """Wires a mocked MT5 + time into the trade_events module and runs the RPC.

    ``polls`` is a queue of return values for successive ``history_deals_get``
    calls; once exhausted it returns ``[]``. ``stop_after_cycles`` flips the
    context inactive after that many completed poll cycles (a cycle ends at the
    ``time.sleep`` call), which is how a well-behaved stream is terminated in a
    test without blocking forever.
    """

    def __init__(self, monkeypatch, polls=None, stop_after_cycles=3,
                 last_error=(-10005, "no history"), context=None,
                 raise_on_poll=False):
        self.polls = list(polls) if polls is not None else []
        self.stop_after_cycles = stop_after_cycles
        self.cycles = 0
        self.history_calls = 0
        self.sleeps = []
        self.last_error_value = last_error
        self.raise_on_poll = raise_on_poll
        self.context = context if context is not None else FakeContext()

        monkeypatch.setattr(te.mt5, "history_deals_get", self._history_deals_get)
        monkeypatch.setattr(te.mt5, "last_error", self._last_error)
        monkeypatch.setattr(te.time, "time", lambda: NOW_MS / 1000.0)
        monkeypatch.setattr(te.time, "sleep", self._sleep)

    def _history_deals_get(self, *args, **kwargs):
        self.history_calls += 1
        if self.raise_on_poll:
            raise RuntimeError("boom")
        if self.polls:
            return self.polls.pop(0)
        return []

    def _last_error(self):
        return self.last_error_value

    def _sleep(self, seconds):
        self.sleeps.append(seconds)
        self.cycles += 1
        if self.stop_after_cycles is not None and self.cycles >= self.stop_after_cycles:
            self.context.active = False

    def run(self, request):
        servicer = te.TradeEventsServiceImpl()
        return list(servicer.SubscribeTradeTransactions(request, self.context))


# --------------------------------------------------------------------------- #
# User Story 1 — live trade transactions (P1)
# --------------------------------------------------------------------------- #

def test_us1_exactly_once_ordered_with_same_ms_ties(monkeypatch):
    """SC-003 / FR-006: 100+ deals across bursts, including same-millisecond ties,
    delivered exactly once, in (time_msc, ticket) order, no dups/omissions."""
    start = NOW_MS - 100_000
    # 100 deals, 10 sharing each millisecond (ties), unique tickets, all after start.
    deals = []
    for i in range(100):
        deals.append(make_deal(ticket=1000 + i, time_msc=start + 1000 + (i // 10)))
    shuffled = list(reversed(deals))  # deterministic non-sorted input

    # poll1 delivers the burst; poll2 re-fetches the SAME deals (boundary re-fetch)
    # and must dedupe to zero; poll3 empty.
    harness = Harness(monkeypatch, polls=[shuffled, list(shuffled), []], stop_after_cycles=3)
    request = SubscribeTradeTransactionsRequest(from_time_msc=start)

    events = harness.run(request)

    assert len(events) == 100, "each deal delivered exactly once"
    tickets = [e.deal_ticket for e in events]
    assert len(set(tickets)) == 100, "no duplicates"
    keys = [(e.time_msc, e.deal_ticket) for e in events]
    assert keys == sorted(keys), "chronological (time_msc, ticket) order"


def test_us1_deal_fields_mapped(monkeypatch):
    """FR-003: event carries the required fields, verbatim type/entry."""
    start = NOW_MS - 10_000
    d = make_deal(ticket=42, time_msc=start + 500, type=1, entry=2,
                  order=7, position_id=9, symbol="GBPUSD", volume=2.5,
                  price=1.2345, profit=12.5)
    harness = Harness(monkeypatch, polls=[[d]], stop_after_cycles=1)
    request = SubscribeTradeTransactionsRequest(from_time_msc=start)

    events = harness.run(request)

    assert len(events) == 1
    e = events[0]
    assert e.deal_ticket == 42
    assert e.order_ticket == 7
    assert e.position_ticket == 9
    assert e.symbol == "GBPUSD"
    assert e.volume == pytest.approx(2.5)
    assert e.price == pytest.approx(1.2345)
    assert e.profit == pytest.approx(12.5)
    assert e.time_msc == start + 500
    assert e.type == 1
    assert e.entry == 2
    assert not e.HasField("error")


def test_us1_no_new_deals_stays_open(monkeypatch):
    """US1 acceptance #3: no deals -> no events, stream stays open across cycles."""
    harness = Harness(monkeypatch, polls=[[], [], []], stop_after_cycles=3)
    request = SubscribeTradeTransactionsRequest(from_time_msc=NOW_MS - 10_000)

    events = harness.run(request)

    assert events == []
    assert harness.history_calls == 3, "kept polling while healthy"


def test_us1_lookup_returns_none_emits_error_then_ends(monkeypatch):
    """FR-009: history_deals_get returns None -> one in-band Error event, stream ends."""
    harness = Harness(monkeypatch, polls=[None], stop_after_cycles=None,
                      last_error=(-10005, "terminal: no history data"))
    request = SubscribeTradeTransactionsRequest(from_time_msc=NOW_MS - 10_000)

    events = harness.run(request)

    assert len(events) == 1
    assert events[0].HasField("error")
    assert events[0].error.code == -10005
    assert "terminal: no history data" in events[0].error.message


def test_us1_poll_exception_emits_error_then_ends(monkeypatch):
    """FR-009: an unexpected exception during a poll surfaces as an in-band Error."""
    harness = Harness(monkeypatch, raise_on_poll=True, stop_after_cycles=None,
                      last_error=(-1, "fail"))
    request = SubscribeTradeTransactionsRequest(from_time_msc=NOW_MS - 10_000)

    events = harness.run(request)

    assert len(events) == 1
    assert events[0].HasField("error")


# --------------------------------------------------------------------------- #
# User Story 2 — start without replaying stale history (P1)
# --------------------------------------------------------------------------- #

def test_us2_default_start_now_delivers_zero_history(monkeypatch):
    """SC-002 / FR-005: unset from_time_msc -> no historical deals delivered."""
    historical = [make_deal(ticket=i, time_msc=NOW_MS - 60_000 + i) for i in range(1, 6)]
    harness = Harness(monkeypatch, polls=[historical, historical, []], stop_after_cycles=3)
    request = SubscribeTradeTransactionsRequest()  # no start time -> now

    events = harness.run(request)

    assert events == [], "historical deals before 'now' are not replayed"


@pytest.mark.parametrize("server_offset_ms", [
    3 * 3600 * 1000,    # broker server time ahead of host UTC (e.g. EET, UTC+3)
    -5 * 3600 * 1000,   # broker server time behind host UTC (e.g. UTC-5)
])
def test_us2_default_start_now_delivers_new_deal_across_clock_offset(monkeypatch, server_offset_ms):
    """Regression: MT5 deal times are in the broker server-time base, which may be
    offset from the host UTC clock. A deal created after subscription (empty prior
    history) must still be delivered under default 'start now', regardless of the
    offset's sign — the watermark must not be a host-UTC 'now' floor."""
    # Empty history at baseline, then a live deal whose time_msc reflects server time.
    live = make_deal(ticket=500, time_msc=NOW_MS + server_offset_ms)
    harness = Harness(monkeypatch, polls=[[], [live], []], stop_after_cycles=3)
    request = SubscribeTradeTransactionsRequest()  # no start time -> now

    events = harness.run(request)

    assert [e.deal_ticket for e in events] == [500], "new deal delivered despite clock offset"


def test_us2_default_start_now_baselines_existing_then_delivers_new(monkeypatch):
    """Default 'start now' on an account with prior history: the existing deals are
    absorbed as the baseline (no replay) and only a subsequently-appearing deal is
    delivered, with the watermark tracked in the deals' own time base."""
    existing = [make_deal(ticket=10, time_msc=NOW_MS + 3_600_000),
                make_deal(ticket=11, time_msc=NOW_MS + 3_600_500)]
    new = make_deal(ticket=12, time_msc=NOW_MS + 3_601_000)
    harness = Harness(monkeypatch, polls=[existing, existing + [new], []], stop_after_cycles=3)
    request = SubscribeTradeTransactionsRequest()

    events = harness.run(request)

    assert [e.deal_ticket for e in events] == [12], "no replay of baseline history; new deal delivered"


def test_us2_explicit_past_start_backfills_once_in_order(monkeypatch):
    """US2 acceptance #2: explicit past start backfills once, in order, then live."""
    start = NOW_MS - 10_000
    backfill = [
        make_deal(ticket=3, time_msc=start + 3000),
        make_deal(ticket=1, time_msc=start + 1000),
        make_deal(ticket=2, time_msc=start + 2000),
    ]
    live = [make_deal(ticket=4, time_msc=start + 4000)]
    harness = Harness(monkeypatch, polls=[backfill, live, []], stop_after_cycles=3)
    request = SubscribeTradeTransactionsRequest(from_time_msc=start)

    events = harness.run(request)

    assert [e.deal_ticket for e in events] == [1, 2, 3, 4], "backfill in order, then live"


def test_us2_start_older_than_7_days_is_clamped_forward(monkeypatch):
    """FR-004: a start older than now-7d is clamped forward to the 7-day cap."""
    harness = Harness(monkeypatch, polls=[[]], stop_after_cycles=1)
    servicer = te.TradeEventsServiceImpl()

    eight_days_ago = NOW_MS - 8 * 24 * 3600 * 1000
    resolved = servicer._resolve_start_ms(
        SubscribeTradeTransactionsRequest(from_time_msc=eight_days_ago), NOW_MS)
    assert resolved == NOW_MS - te.MAX_BACKFILL_MS

    # A start within the cap is preserved.
    six_days_ago = NOW_MS - 6 * 24 * 3600 * 1000
    resolved_within = servicer._resolve_start_ms(
        SubscribeTradeTransactionsRequest(from_time_msc=six_days_ago), NOW_MS)
    assert resolved_within == six_days_ago


def test_us2_deals_before_clamped_start_are_dropped(monkeypatch):
    """FR-004 behavior: deals older than the 7-day cap are not delivered."""
    start = NOW_MS - 8 * 24 * 3600 * 1000  # will clamp to now-7d
    cap = NOW_MS - te.MAX_BACKFILL_MS
    before_cap = make_deal(ticket=1, time_msc=cap - 3600_000)   # dropped
    after_cap = make_deal(ticket=2, time_msc=cap + 3600_000)    # delivered
    harness = Harness(monkeypatch, polls=[[before_cap, after_cap], []], stop_after_cycles=2)
    request = SubscribeTradeTransactionsRequest(from_time_msc=start)

    events = harness.run(request)

    assert [e.deal_ticket for e in events] == [2]


# --------------------------------------------------------------------------- #
# Cadence clamping (FR-007)
# --------------------------------------------------------------------------- #

def test_cadence_defaults_to_1000_when_unset(monkeypatch):
    harness = Harness(monkeypatch, polls=[[]], stop_after_cycles=1)
    servicer = te.TradeEventsServiceImpl()
    assert servicer._resolve_cadence_ms(SubscribeTradeTransactionsRequest()) == 1000


def test_cadence_below_floor_is_clamped_to_200(monkeypatch):
    servicer = te.TradeEventsServiceImpl()
    assert servicer._resolve_cadence_ms(
        SubscribeTradeTransactionsRequest(poll_interval_ms=50)) == 200
    assert servicer._resolve_cadence_ms(
        SubscribeTradeTransactionsRequest(poll_interval_ms=500)) == 500


def test_cadence_is_used_for_sleep(monkeypatch):
    """The resolved cadence (seconds) is passed to time.sleep between polls."""
    harness = Harness(monkeypatch, polls=[[], []], stop_after_cycles=2)
    request = SubscribeTradeTransactionsRequest(from_time_msc=NOW_MS - 1000,
                                                poll_interval_ms=200)
    harness.run(request)
    assert all(s == pytest.approx(0.2) for s in harness.sleeps)


# --------------------------------------------------------------------------- #
# User Story 3 — clean shutdown and resubscription (P2)
# --------------------------------------------------------------------------- #

def test_us3_cancellation_stops_polling_promptly(monkeypatch):
    """SC-004 / FR-008: once the context goes inactive, no further polls occur."""
    start = NOW_MS - 10_000
    harness = Harness(monkeypatch, polls=[[make_deal(ticket=1, time_msc=start + 1)]],
                      stop_after_cycles=1)  # inactive after the first cycle
    request = SubscribeTradeTransactionsRequest(from_time_msc=start)

    events = harness.run(request)

    assert len(events) == 1
    assert harness.history_calls == 1, "no polling after cancellation"


def test_us3_cancellation_mid_batch_stops_yielding(monkeypatch):
    """FR-008: cancelling mid-batch stops delivering the remaining deals promptly."""
    start = NOW_MS - 10_000
    batch = [make_deal(ticket=i, time_msc=start + i) for i in range(1, 6)]
    # is_active(): 1 (while) + True for first 2 pre-yield checks, then False.
    context = FakeContext(max_active_calls=3)
    harness = Harness(monkeypatch, polls=[batch], stop_after_cycles=None, context=context)
    request = SubscribeTradeTransactionsRequest(from_time_msc=start)

    events = harness.run(request)

    assert len(events) < len(batch), "did not deliver the whole batch after cancel"


def test_us3_resume_from_last_time_no_gap_no_duplicate(monkeypatch):
    """US3 acceptance #2: resuming from the last received time L delivers every
    transaction after L with no duplicate of the transaction at L."""
    # Subscription A ends having delivered a deal at time L (ticket 10).
    L = NOW_MS - 5_000
    boundary = make_deal(ticket=10, time_msc=L)
    after = make_deal(ticket=11, time_msc=L + 1000)
    # Resume: a fresh subscription from_time_msc = L. The boundary second is
    # re-fetched (returns both), but the deal AT L must be dropped as already-seen.
    harness = Harness(monkeypatch, polls=[[boundary, after], []], stop_after_cycles=2)
    request = SubscribeTradeTransactionsRequest(from_time_msc=L)

    events = harness.run(request)

    assert [e.deal_ticket for e in events] == [11], "no duplicate of the deal at L, no gap after L"
