from typing import Optional, Tuple

import grpc

from ..bridge.client import BridgeUnavailable
from ..mt5_source import mt5
from mt5_grpc_proto.deal_pb2 import (
    DealsRequest,
    DealsResponse,
    Deal
)
from mt5_grpc_proto.deal_pb2_grpc import TradeHistoryServiceServicer
from mt5_grpc_proto.common_pb2 import Error


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

    def GetDeals(self, request: DealsRequest, context) -> DealsResponse:
        """Get deals from MT5 based on specified filters.

        According to MT5 reference, we can filter by:
        1. Time interval using date_from and date_to
        2. Symbol group using the group parameter
        3. Specific ticket
        4. Position ID

        Args:
            request: DealsRequest containing filter criteria
            context: gRPC context

        Returns:
            DealsResponse containing matched deals or error
        """
        response = DealsResponse()

        try:
            # Handle different filter types according to MT5 reference
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
                # If no filters specified, return error
                response.error.code = -2  # RES_E_INVALID_PARAMS
                response.error.message = "No valid filter criteria provided"
                return response

            if deals is None:
                error_code, error_message = mt5.last_error()
                response.error.code = error_code
                response.error.message = f"Failed to get deals: {error_message}"
                return response

            # Convert MT5 deals to protobuf messages
            for mt5_deal in deals:
                deal_proto = self._convert_deal_to_proto(mt5_deal)
                response.deals.append(deal_proto)

            return response

        except BridgeUnavailable as e:
            # The Wine-side worker is not reachable, so this is a transport
            # problem rather than an MT5 result. Report it out of band so the
            # caller can retry instead of reading an empty deal list as truth.
            context.abort(grpc.StatusCode.UNAVAILABLE, str(e))

        except Exception as e:
            response.error.code = -1  # RES_E_FAIL
            response.error.message = f"Internal error processing deals: {str(e)}"
            return response
