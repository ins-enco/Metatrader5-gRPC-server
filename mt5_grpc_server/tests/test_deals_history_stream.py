"""Chunking tests for TradeHistoryServiceImpl.StreamDeals.

All tests drive the server-streaming RPC against a **mock** ``history_deals_get``
(no live broker), asserting chunk boundaries for exact and non-exact multiples of
the chunk size, ``_chunk_size_for`` clamping, an empty history yielding nothing,
error-only output on failure, and early exit when the caller goes away.

``StreamDeals`` shipped in 95e3317 without a test; ``0.4.0`` is the release that
advertises it, and the constitution requires a no-live-broker test path for new
RPC behaviour. These tests are read-only with respect to the server: they assert
existing behaviour and change none of it.

Covers: specs/007-csharp-stream-deals research Decision 7.
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

from mt5_grpc_server.imp import deals_history as dh  # noqa: E402
from mt5_grpc_proto.deal_pb2 import DealsRequest  # noqa: E402


FakeDeal = namedtuple(
    "FakeDeal",
    "ticket order time time_msc type entry magic position_id reason volume price "
    "commission swap profit fee symbol comment external_id",
)


def make_deal(ticket, symbol="EURUSD", order=None, position_id=None):
    return FakeDeal(
        ticket=ticket,
        order=order if order is not None else ticket,
        time=1_700_000_000 + ticket,
        time_msc=(1_700_000_000 + ticket) * 1000,
        type=0,
        entry=0,
        magic=0,
        position_id=position_id if position_id is not None else ticket,
        reason=0,
        volume=1.0,
        price=1.1,
        commission=0.0,
        swap=0.0,
        profit=0.0,
        fee=0.0,
        symbol=symbol,
        comment="",
        external_id="",
    )


class FakeContext:
    """Minimal gRPC servicer context stand-in.

    ``is_active()`` returns True for ``max_active_calls`` calls then False, which
    is how a caller going away mid-stream is simulated.
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
    """Wires a mocked MT5 into the deals_history module and runs the RPC.

    ``deals`` is what ``history_deals_get`` returns; ``None`` models an MT5
    failure, for which ``last_error`` supplies the reported code and message.
    """

    def __init__(self, monkeypatch, deals=(), last_error=(-10005, "no history"),
                 context=None, raise_on_fetch=False):
        self.deals = deals
        self.last_error_value = last_error
        self.raise_on_fetch = raise_on_fetch
        self.context = context if context is not None else FakeContext()
        self.calls = []

        monkeypatch.setattr(dh.mt5, "history_deals_get", self._history_deals_get)
        monkeypatch.setattr(dh.mt5, "last_error", self._last_error)

    def _history_deals_get(self, *args, **kwargs):
        self.calls.append((args, kwargs))
        if self.raise_on_fetch:
            raise RuntimeError("boom")
        return self.deals

    def _last_error(self):
        return self.last_error_value

    def stream(self, request):
        servicer = dh.TradeHistoryServiceImpl()
        return list(servicer.StreamDeals(request, self.context))

    def unary(self, request):
        servicer = dh.TradeHistoryServiceImpl()
        return servicer.GetDeals(request, self.context)


def whole_history():
    request = DealsRequest()
    request.time_filter.date_from = 0
    request.time_filter.date_to = 0
    return request


# --------------------------------------------------------------------------- #
# Chunk boundaries
# --------------------------------------------------------------------------- #

def test_non_exact_multiple_of_chunk_size_splits_with_a_short_final_chunk(monkeypatch):
    """1571 deals at the 500 default: 500 + 500 + 500 + 71, in order."""
    deals = [make_deal(t) for t in range(1, 1572)]
    harness = Harness(monkeypatch, deals=deals)

    chunks = harness.stream(whole_history())

    assert [len(chunk.deals) for chunk in chunks] == [500, 500, 500, 71]
    streamed = [deal.ticket for chunk in chunks for deal in chunk.deals]
    assert streamed == [deal.ticket for deal in deals]
    assert all(not chunk.HasField("error") for chunk in chunks)


def test_exact_multiple_of_chunk_size_emits_no_trailing_empty_chunk(monkeypatch):
    """1000 deals at 500: exactly two full chunks and nothing more."""
    deals = [make_deal(t) for t in range(1, 1001)]
    harness = Harness(monkeypatch, deals=deals)

    chunks = harness.stream(whole_history())

    assert [len(chunk.deals) for chunk in chunks] == [500, 500]


def test_single_deal_yields_one_chunk(monkeypatch):
    harness = Harness(monkeypatch, deals=[make_deal(1)])

    chunks = harness.stream(whole_history())

    assert len(chunks) == 1
    assert [deal.ticket for deal in chunks[0].deals] == [1]


def test_streamed_concatenation_equals_the_unary_result(monkeypatch):
    """The two RPCs share _fetch_deals, so they must agree deal for deal."""
    deals = [make_deal(t) for t in range(1, 1572)]

    harness = Harness(monkeypatch, deals=deals)
    streamed = [deal for chunk in harness.stream(whole_history()) for deal in chunk.deals]

    harness = Harness(monkeypatch, deals=deals)
    unary = list(harness.unary(whole_history()).deals)

    assert [deal.ticket for deal in streamed] == [deal.ticket for deal in unary]
    assert streamed == unary


# --------------------------------------------------------------------------- #
# _chunk_size_for clamping
# --------------------------------------------------------------------------- #

@pytest.mark.parametrize(
    "requested, expected",
    [
        (None, dh.DEFAULT_CHUNK_SIZE),   # unset -> default
        (0, dh.DEFAULT_CHUNK_SIZE),      # explicit zero is treated as unset
        (250, 250),                      # below the cap, honoured as asked
        (1000, dh.MAX_CHUNK_SIZE),       # exactly the cap
        (5000, dh.MAX_CHUNK_SIZE),       # above the cap -> clamped down
    ],
)
def test_chunk_size_for_clamps_to_the_documented_bounds(requested, expected):
    request = whole_history()
    if requested is not None:
        request.chunk_size = requested

    assert dh.TradeHistoryServiceImpl()._chunk_size_for(request) == expected


def test_default_and_cap_are_the_documented_values():
    assert dh.DEFAULT_CHUNK_SIZE == 500
    assert dh.MAX_CHUNK_SIZE == 1000


@pytest.mark.parametrize(
    "requested, expected_sizes",
    [
        (250, [250, 250, 100]),
        (5000, [1000, 1000, 500]),   # clamped to 1000, not honoured at 5000
    ],
)
def test_requested_chunk_size_shapes_the_stream(monkeypatch, requested, expected_sizes):
    deals = [make_deal(t) for t in range(1, 601 if requested == 250 else 2501)]
    harness = Harness(monkeypatch, deals=deals)

    request = whole_history()
    request.chunk_size = requested
    chunks = harness.stream(request)

    assert [len(chunk.deals) for chunk in chunks] == expected_sizes
    # Every deal still reaches the caller regardless of the clamp.
    assert sum(len(chunk.deals) for chunk in chunks) == len(deals)


def test_get_deals_ignores_chunk_size(monkeypatch):
    """chunk_size is StreamDeals only; the unary RPC must not react to it."""
    deals = [make_deal(t) for t in range(1, 1201)]
    harness = Harness(monkeypatch, deals=deals)

    request = whole_history()
    request.chunk_size = 10
    response = harness.unary(request)

    assert len(response.deals) == 1200


# --------------------------------------------------------------------------- #
# Empty history
# --------------------------------------------------------------------------- #

def test_empty_history_yields_no_message_at_all(monkeypatch):
    """Zero chunks is a successful, complete read - never an error frame."""
    harness = Harness(monkeypatch, deals=[])

    chunks = harness.stream(whole_history())

    assert chunks == []


# --------------------------------------------------------------------------- #
# Error reporting
# --------------------------------------------------------------------------- #

def test_mt5_failure_yields_exactly_one_error_only_message(monkeypatch):
    """history_deals_get returning None is an MT5 failure, reported in band."""
    harness = Harness(monkeypatch, deals=None, last_error=(-10005, "no history"))

    chunks = harness.stream(whole_history())

    assert len(chunks) == 1
    assert len(chunks[0].deals) == 0
    assert chunks[0].error.code == -10005
    assert "no history" in chunks[0].error.message


def test_no_filter_criteria_yields_the_documented_error(monkeypatch):
    """An empty request is the server's -2, not a crash and not an empty read."""
    harness = Harness(monkeypatch, deals=[make_deal(1)])

    chunks = harness.stream(DealsRequest())

    assert len(chunks) == 1
    assert chunks[0].error.code == -2
    assert chunks[0].error.message == "No valid filter criteria provided"
    # The filter never reached MT5.
    assert harness.calls == []


def test_unexpected_exception_yields_one_error_only_message(monkeypatch):
    harness = Harness(monkeypatch, raise_on_fetch=True)

    chunks = harness.stream(whole_history())

    assert len(chunks) == 1
    assert len(chunks[0].deals) == 0
    assert chunks[0].error.code == -1
    assert "Internal error processing deals" in chunks[0].error.message


# --------------------------------------------------------------------------- #
# Early exit when the caller goes away
# --------------------------------------------------------------------------- #

def test_stream_stops_converting_once_the_context_goes_inactive(monkeypatch):
    """is_active() is checked per chunk, so an abandoned call stops early."""
    deals = [make_deal(t) for t in range(1, 2501)]   # 5 chunks at the default
    harness = Harness(
        monkeypatch,
        deals=deals,
        context=FakeContext(max_active_calls=2),
    )

    chunks = harness.stream(whole_history())

    # Two chunks converted, then the loop returns instead of finishing the rest.
    assert len(chunks) == 2
    assert sum(len(chunk.deals) for chunk in chunks) == 1000


def test_stream_yields_nothing_when_the_caller_is_already_gone(monkeypatch):
    harness = Harness(
        monkeypatch,
        deals=[make_deal(t) for t in range(1, 1001)],
        context=FakeContext(active=False),
    )

    assert harness.stream(whole_history()) == []


# --------------------------------------------------------------------------- #
# Filter forms reach MT5 exactly as GetDeals sends them
# --------------------------------------------------------------------------- #

def test_time_filter_passes_the_window_and_group_through(monkeypatch):
    harness = Harness(monkeypatch, deals=[make_deal(1)])

    request = DealsRequest()
    request.time_filter.date_from = 111
    request.time_filter.date_to = 222
    request.group = "EUR*"
    harness.stream(request)

    args, kwargs = harness.calls[0]
    assert args == (111, 222)
    assert kwargs == {"group": "EUR*"}


def test_time_filter_substitutes_the_wildcard_group_when_unset(monkeypatch):
    harness = Harness(monkeypatch, deals=[make_deal(1)])

    request = DealsRequest()
    request.time_filter.date_from = 111
    request.time_filter.date_to = 222
    harness.stream(request)

    _, kwargs = harness.calls[0]
    assert kwargs == {"group": "*"}


def test_ticket_filter_is_forwarded_by_keyword(monkeypatch):
    harness = Harness(monkeypatch, deals=[make_deal(1)])

    request = DealsRequest()
    request.ticket = 4242
    harness.stream(request)

    args, kwargs = harness.calls[0]
    assert args == ()
    assert kwargs == {"ticket": 4242}


def test_position_filter_is_forwarded_by_keyword(monkeypatch):
    harness = Harness(monkeypatch, deals=[make_deal(1)])

    request = DealsRequest()
    request.position = 909
    harness.stream(request)

    args, kwargs = harness.calls[0]
    assert args == ()
    assert kwargs == {"position": 909}
