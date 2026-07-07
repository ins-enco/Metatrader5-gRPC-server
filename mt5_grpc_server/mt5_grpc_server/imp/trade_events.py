import time
import datetime
import MetaTrader5 as mt5
from mt5_grpc_proto.trade_events_pb2 import (
    SubscribeTradeTransactionsRequest,
    TradeTransactionEvent,
)
from mt5_grpc_proto.trade_events_pb2_grpc import TradeEventsServiceServicer
from mt5_grpc_proto.common_pb2 import Error


# Server-enforced poll cadence floor (ms) — protects the terminal from excessive
# querying (FR-007).
MIN_POLL_INTERVAL_MS = 200
# Default poll cadence (ms) when the client does not specify one (FR-007).
DEFAULT_POLL_INTERVAL_MS = 1000
# Maximum historical backfill window (ms) — bounds one-shot replay and start-up
# cost (FR-004).
MAX_BACKFILL_MS = 7 * 24 * 3600 * 1000
# Larger than any real MT5 deal ticket; used as the initial watermark ticket so
# the resolved start time is treated as "already seen up to this instant". This
# makes the default-start-now case replay nothing and a resume from the last
# received time L drop the (already-delivered) deal at exactly L (Decision 3).
_UINT64_MAX = (1 << 64) - 1


class TradeEventsServiceImpl(TradeEventsServiceServicer):
    """Streams trade transaction events (newly added deals) for the connected
    MT5 account.

    The MetaTrader 5 Python API exposes no push/event callback, so events are
    emulated by polling ``history_deals_get`` over an advancing time window.
    Each subscription tracks a ``(time_msc, deal_ticket)`` watermark so it
    delivers every qualifying deal exactly once, in chronological order, with no
    duplicates (de-duplicated on the globally unique deal ticket, so
    same-millisecond deals are both delivered).
    """

    def __init__(self):
        pass

    def _now_ms(self) -> int:
        """Current server time in milliseconds since the Unix epoch."""
        return int(time.time() * 1000)

    def _resolve_start_ms(self, request: SubscribeTradeTransactionsRequest, now_ms: int) -> int:
        """Resolve the subscription start point in ms (FR-004, FR-005).

        - unset or 0 ``from_time_msc`` -> start now (no historical replay).
        - an explicit past time older than ``now - 7 days`` -> clamp forward to
          the 7-day backfill cap.
        """
        if not request.HasField('from_time_msc') or request.from_time_msc <= 0:
            return now_ms
        return max(request.from_time_msc, now_ms - MAX_BACKFILL_MS)

    def _resolve_cadence_ms(self, request: SubscribeTradeTransactionsRequest) -> int:
        """Resolve poll cadence in ms (FR-007): unset -> 1000; below floor -> 200."""
        if not request.HasField('poll_interval_ms'):
            return DEFAULT_POLL_INTERVAL_MS
        return max(request.poll_interval_ms, MIN_POLL_INTERVAL_MS)

    def _convert_deal_to_event(self, mt5_deal) -> TradeTransactionEvent:
        """Map an MT5 deal to a TradeTransactionEvent (FR-002, FR-003).

        Direction (``type``) and entry are passed through verbatim from MT5
        (Constitution II — no remap), mirroring the existing ``Deal`` message.
        """
        return TradeTransactionEvent(
            deal_ticket=mt5_deal.ticket,
            order_ticket=mt5_deal.order,
            position_ticket=mt5_deal.position_id,
            symbol=mt5_deal.symbol,
            volume=float(mt5_deal.volume),
            price=float(mt5_deal.price),
            profit=float(mt5_deal.profit),
            time_msc=mt5_deal.time_msc,
            type=mt5_deal.type,
            entry=mt5_deal.entry,
        )

    def _error_event(self, message_prefix: str) -> TradeTransactionEvent:
        """Build a terminal in-band Error event from ``mt5.last_error()`` (FR-009)."""
        error_code, error_message = mt5.last_error()
        event = TradeTransactionEvent()
        event.error.code = error_code
        event.error.message = f"{message_prefix}: {error_message}"
        return event

    def SubscribeTradeTransactions(self, request: SubscribeTradeTransactionsRequest, context):
        """Server-streaming RPC: yield one TradeTransactionEvent per newly added
        deal until the client cancels/disconnects or a terminal failure occurs.

        Loop: poll ``history_deals_get`` over an advancing window -> emit the new
        deals in ``(time_msc, ticket)`` order -> advance the watermark ->
        ``sleep(cadence)``. Cancellation/disconnect (``context.is_active()`` is
        false) ends the loop promptly, releasing the worker (FR-008, SC-004). A
        terminal/persistent lookup failure emits one in-band ``Error`` event and
        ends the stream (FR-009).
        """
        now_ms = self._now_ms()
        start_ms = self._resolve_start_ms(request, now_ms)
        cadence_ms = self._resolve_cadence_ms(request)
        cadence_seconds = cadence_ms / 1000.0

        # Watermark = last delivered (time_msc, ticket). Initialised to the resolved
        # start with a sentinel ticket so nothing at or before the start instant is
        # replayed (default-start-now delivers zero history; resume drops the deal
        # at exactly the resume time). Live ties are then handled by the real
        # (time_msc, ticket) tuple as the watermark advances (FR-006, Decision 3).
        watermark = (start_ms, _UINT64_MAX)

        while context.is_active():
            # Query from the watermark's floored second: MT5 time filters are
            # second-granular, so querying from the second is a superset; the
            # precise tuple filter below removes the re-fetched boundary deals,
            # guaranteeing no gap and no duplicate.
            date_from = datetime.datetime.fromtimestamp(
                watermark[0] // 1000, tz=datetime.timezone.utc)
            date_to = datetime.datetime.fromtimestamp(
                self._now_ms() // 1000 + 1, tz=datetime.timezone.utc)

            try:
                deals = mt5.history_deals_get(date_from, date_to)
            except Exception as exc:  # pragma: no cover - defensive
                yield self._error_event(f"Failed to poll trade transactions: {exc}")
                return

            if deals is None:
                # Terminal/persistent lookup failure (or terminal not initialised).
                yield self._error_event("Failed to get trade transactions")
                return

            new_deals = sorted(
                (d for d in deals if (d.time_msc, d.ticket) > watermark),
                key=lambda d: (d.time_msc, d.ticket),
            )

            for mt5_deal in new_deals:
                if not context.is_active():
                    return
                yield self._convert_deal_to_event(mt5_deal)
                watermark = (mt5_deal.time_msc, mt5_deal.ticket)

            time.sleep(cadence_seconds)
