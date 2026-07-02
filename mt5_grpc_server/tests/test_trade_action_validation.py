"""Server-side validation of the trade-request action (FR-014, SC-009).

An unset or TRADE_ACTION_UNSPECIFIED (0) action must be rejected with a
structured error and must place no order. See specs/003-csharp-request-enums.
"""
import sys
import unittest
from unittest.mock import MagicMock

# Mock the MetaTrader5 import before importing the server implementation.
sys.modules['MetaTrader5'] = MagicMock()

from mt5_grpc_proto.trade_pb2 import OrderSendRequest, TradeRequest  # noqa: E402
from mt5_grpc_proto.trade_pb2 import (  # noqa: E402
    TRADE_ACTION_UNSPECIFIED,
    TRADE_ACTION_DEAL,
)
from mt5_grpc_server.imp import trade as trade_impl  # noqa: E402
from mt5_grpc_server.imp.trade import OrderSendServiceImpl  # noqa: E402


class TestTradeActionValidation(unittest.TestCase):
    def setUp(self):
        # Use the exact MetaTrader5 mock the implementation module bound at import
        # time; other test modules may reassign sys.modules['MetaTrader5'].
        self.mt5 = trade_impl.mt5
        self.mt5.reset_mock()
        self.service = OrderSendServiceImpl()
        self.context = MagicMock()

    def test_unset_action_is_rejected_and_places_no_order(self):
        # Action left unset -> defaults to TRADE_ACTION_UNSPECIFIED (0).
        request = OrderSendRequest(trade_request=TradeRequest(symbol="EURUSD", volume=0.1))

        response = self.service.SendOrder(request, self.context)

        self.assertNotEqual(response.error.code, 0)
        self.assertTrue(response.error.message)
        self.assertFalse(response.HasField("trade_result"))
        self.mt5.order_send.assert_not_called()

    def test_unspecified_action_is_rejected_and_places_no_order(self):
        request = OrderSendRequest(
            trade_request=TradeRequest(action=TRADE_ACTION_UNSPECIFIED, symbol="EURUSD", volume=0.1)
        )

        response = self.service.SendOrder(request, self.context)

        self.assertNotEqual(response.error.code, 0)
        self.assertFalse(response.HasField("trade_result"))
        self.mt5.order_send.assert_not_called()

    def test_valid_action_reaches_mt5_order_send(self):
        # A real action must NOT be short-circuited by the validation guard.
        self.mt5.order_send.return_value = None       # emulate MT5 rejecting for other reasons
        self.mt5.last_error.return_value = (123, "some mt5 error")

        request = OrderSendRequest(
            trade_request=TradeRequest(action=TRADE_ACTION_DEAL, symbol="EURUSD", volume=0.1)
        )

        response = self.service.SendOrder(request, self.context)

        self.mt5.order_send.assert_called_once()
        # order_send returned None -> error surfaced from self.mt5.last_error, not the guard.
        self.assertEqual(response.error.code, 123)


if __name__ == '__main__':
    unittest.main()
