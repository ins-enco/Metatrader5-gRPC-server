import MetaTrader5 as mt5
from typing import Optional, Tuple
from mt5_grpc_proto.deal_pb2 import (
    DealsRequest,
    DealsResponse,
    Deal
)
from mt5_grpc_proto.deal_pb2_grpc import TradeHistoryServiceServicer
from mt5_grpc_proto.common_pb2 import Error


# StreamDeals chunking. A single response holding a few thousand deals is
# large enough to trigger a partial socket write, which grpcio aborts on
# under Wine, so keep each streamed message well below that.
#
# Measured against a live account on 07/09/2026: 1571 deals serialise to
# 121 KB and abort the server every time; 982 deals (~75 KB) never did. The
# cap is set just under the largest size observed to survive, and the
# default leaves roughly a threefold margin (500 deals ~ 39 KB).
DEFAULT_CHUNK_SIZE = 500
MAX_CHUNK_SIZE = 1000


class TradeHistoryServiceImpl(TradeHistoryServiceServicer):
    def __init__(self):
        pass

    def _convert_deal_to_proto(self, mt5_deal) -> Deal:
        """Convert MT5 deal object to protobuf Deal message.

        Args:
            mt5_deal: Deal information from MT5

        Returns:
            Deal: Protobuf Deal message
        """
        # Use the exact field names from the MT5 reference
        return Deal(
            ticket=mt5_deal.ticket,
            order=mt5_deal.order,
            time=int(mt5_deal.time),  # MT5 returns Unix timestamp
            time_msc=mt5_deal.time_msc,
            type=mt5_deal.type,
            entry=mt5_deal.entry,
            magic=mt5_deal.magic,
            position_id=mt5_deal.position_id,
            reason=mt5_deal.reason,
            volume=float(mt5_deal.volume),
            price=float(mt5_deal.price),
            commission=float(mt5_deal.commission),
            swap=float(mt5_deal.swap),
            profit=float(mt5_deal.profit),
            fee=float(mt5_deal.fee),
            symbol=mt5_deal.symbol,
            comment=mt5_deal.comment,
            external_id=mt5_deal.external_id
        )

    def _fetch_deals(self, request: DealsRequest):
        """Run the MT5 history query described by a request.

        According to MT5 reference, we can filter by:
        1. Time interval using date_from and date_to
        2. Symbol group using the group parameter
        3. Specific ticket
        4. Position ID

        Args:
            request: DealsRequest containing filter criteria

        Returns:
            (deals, None) on success, or (None, (code, message)) on failure.
        """
        if request.HasField('time_filter'):
            deals = mt5.history_deals_get(
                request.time_filter.date_from,
                request.time_filter.date_to,
                group=request.group if request.HasField('group') else '*'
            )
        elif request.HasField('ticket'):
            # Use ticket parameter as documented
            deals = mt5.history_deals_get(ticket=request.ticket)
        elif request.HasField('position'):
            # Use position parameter as documented
            deals = mt5.history_deals_get(position=request.position)
        else:
            # If no filters specified, report invalid params
            return None, (-2, "No valid filter criteria provided")

        if deals is None:
            error_code, error_message = mt5.last_error()
            return None, (error_code, f"Failed to get deals: {error_message}")

        return deals, None

    def _chunk_size_for(self, request: DealsRequest) -> int:
        """Deals per streamed message, clamped to a size the socket can take."""
        if request.HasField('chunk_size') and request.chunk_size > 0:
            return min(request.chunk_size, MAX_CHUNK_SIZE)
        return DEFAULT_CHUNK_SIZE

    def GetDeals(self, request: DealsRequest, context) -> DealsResponse:
        """Get deals from MT5 based on specified filters.

        The whole result is returned in one message. For a large history use
        StreamDeals instead - see the note on MAX_CHUNK_SIZE above.

        Args:
            request: DealsRequest containing filter criteria
            context: gRPC context

        Returns:
            DealsResponse containing matched deals or error
        """
        response = DealsResponse()

        try:
            deals, error = self._fetch_deals(request)
            if error is not None:
                response.error.code, response.error.message = error
                return response

            # Convert MT5 deals to protobuf messages
            for mt5_deal in deals:
                response.deals.append(self._convert_deal_to_proto(mt5_deal))

            return response

        except Exception as e:
            response.error.code = -1  # RES_E_FAIL
            response.error.message = f"Internal error processing deals: {str(e)}"
            return response

    def StreamDeals(self, request: DealsRequest, context):
        """Same filters as GetDeals, streamed in bounded chunks.

        Each yielded DealsResponse carries at most chunk_size deals, so a
        large history is delivered without ever building one oversized
        message. An empty history completes the stream without yielding.
        Errors are reported as a single response carrying only `error`.

        Args:
            request: DealsRequest containing filter criteria
            context: gRPC context

        Yields:
            DealsResponse chunks, or one DealsResponse holding an error
        """
        try:
            deals, error = self._fetch_deals(request)
        except Exception as e:
            response = DealsResponse()
            response.error.code = -1  # RES_E_FAIL
            response.error.message = f"Internal error processing deals: {str(e)}"
            yield response
            return

        if error is not None:
            response = DealsResponse()
            response.error.code, response.error.message = error
            yield response
            return

        chunk_size = self._chunk_size_for(request)

        for offset in range(0, len(deals), chunk_size):
            # Stop converting if the caller has already gone away
            if context is not None and not context.is_active():
                return

            response = DealsResponse()
            for mt5_deal in deals[offset:offset + chunk_size]:
                response.deals.append(self._convert_deal_to_proto(mt5_deal))
            yield response
