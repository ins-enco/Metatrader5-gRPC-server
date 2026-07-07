import MetaTrader5 as mt5
from mt5_grpc_proto.trade_pb2 import (
    TradeResult,
    OrderSendResponse,
    TRADE_ACTION_UNSPECIFIED,
)
from mt5_grpc_proto.trade_pb2_grpc import OrderSendServiceServicer


class OrderSendServiceImpl(OrderSendServiceServicer):
    def SendOrder(self, request, context):
        """
        Implements the SendOrder RPC method.
        Sends a trading order to MetaTrader 5.
        """
        # Initialize the response
        response = OrderSendResponse()

        # Reject an unset / TRADE_ACTION_UNSPECIFIED (0) action rather than
        # executing it. MT5 defines no action 0, so this is not a valid order;
        # placing one would be unsafe (FR-014, SC-009).
        if request.trade_request.action == TRADE_ACTION_UNSPECIFIED:
            response.error.code = -1
            response.error.message = (
                "Trade request action is unset or TRADE_ACTION_UNSPECIFIED (0); "
                "set an explicit MT5 trade action (e.g. TRADE_ACTION_DEAL)."
            )
            return response

        try:
            # Create MT5 request structure
            mt5_request = {
                "action": request.trade_request.action,
                "magic": request.trade_request.magic,
                "volume": request.trade_request.volume,
                "deviation": request.trade_request.deviation,
                "type": request.trade_request.type,
                "type_filling": request.trade_request.type_filling,
                "type_time": request.trade_request.type_time,
            }

            # Add optional fields if they are set
            if request.trade_request.HasField('order'):
                mt5_request["order"] = request.trade_request.order
            if request.trade_request.HasField('symbol'):
                mt5_request["symbol"] = request.trade_request.symbol
            if request.trade_request.HasField('price'):
                mt5_request["price"] = request.trade_request.price
            if request.trade_request.HasField('stoplimit'):
                mt5_request["stoplimit"] = request.trade_request.stoplimit
            if request.trade_request.HasField('sl'):
                mt5_request["sl"] = request.trade_request.sl
            if request.trade_request.HasField('tp'):
                mt5_request["tp"] = request.trade_request.tp
            if request.trade_request.HasField('expiration'):
                mt5_request["expiration"] = request.trade_request.expiration.seconds
            if request.trade_request.HasField('comment'):
                mt5_request["comment"] = request.trade_request.comment
            if request.trade_request.HasField('position'):
                mt5_request["position"] = request.trade_request.position
            if request.trade_request.HasField('position_by'):
                mt5_request["position_by"] = request.trade_request.position_by

            # Send order to MT5
            result = mt5.order_send(mt5_request)
            
            if result is None:
                error_code, error_message = mt5.last_error()
                response.error.code = error_code
                response.error.message = error_message
                return response

            # Create TradeResult message and populate it with MT5 data
            trade_result = TradeResult()
            trade_result.retcode = result.retcode
            trade_result.deal = result.deal
            trade_result.order = result.order
            trade_result.volume = result.volume
            trade_result.price = result.price
            trade_result.bid = result.bid
            trade_result.ask = result.ask
            trade_result.comment = result.comment
            trade_result.request_id = result.request_id
            trade_result.retcode_external = result.retcode_external

            # Copy the original request
            trade_result.request.CopyFrom(request.trade_request)

            # Set the trade_result field in the response
            response.trade_result.CopyFrom(trade_result)
            
            return response

        except Exception as e:
            response.error.code = -1  # Generic error code for exceptions
            response.error.message = str(e)
            return response
