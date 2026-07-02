using Google.Protobuf;
using Metatrader.V1;
using Xunit;

namespace MetaTrader.Grpc.Client.Tests
{
    /// <summary>
    /// Verifies the native protobuf request enums added in 0.2.0: value fidelity
    /// against the authoritative MT5 numbers, wire round-trip, and open-enum
    /// preservation of unknown values. See specs/003-csharp-request-enums.
    /// </summary>
    public sealed class RequestEnumTests
    {
        // --- Value fidelity (FR-003, SC-002): (int)member == MT5 numeric value ---

        [Theory]
        [InlineData(ENUM_TRADE_REQUEST_ACTIONS.TradeActionUnspecified, 0)]
        [InlineData(ENUM_TRADE_REQUEST_ACTIONS.TradeActionDeal, 1)]
        [InlineData(ENUM_TRADE_REQUEST_ACTIONS.TradeActionPending, 5)]
        [InlineData(ENUM_TRADE_REQUEST_ACTIONS.TradeActionSltp, 6)]
        [InlineData(ENUM_TRADE_REQUEST_ACTIONS.TradeActionModify, 7)]
        [InlineData(ENUM_TRADE_REQUEST_ACTIONS.TradeActionRemove, 8)]
        [InlineData(ENUM_TRADE_REQUEST_ACTIONS.TradeActionCloseBy, 10)]
        public void Trade_action_values_match_mt5(ENUM_TRADE_REQUEST_ACTIONS member, int expected)
        {
            Assert.Equal(expected, (int)member);
        }

        [Theory]
        [InlineData(ENUM_ORDER_TYPE.OrderTypeBuy, 0)]
        [InlineData(ENUM_ORDER_TYPE.OrderTypeSell, 1)]
        [InlineData(ENUM_ORDER_TYPE.OrderTypeBuyLimit, 2)]
        [InlineData(ENUM_ORDER_TYPE.OrderTypeSellLimit, 3)]
        [InlineData(ENUM_ORDER_TYPE.OrderTypeBuyStop, 4)]
        [InlineData(ENUM_ORDER_TYPE.OrderTypeSellStop, 5)]
        [InlineData(ENUM_ORDER_TYPE.OrderTypeBuyStopLimit, 6)]
        [InlineData(ENUM_ORDER_TYPE.OrderTypeSellStopLimit, 7)]
        [InlineData(ENUM_ORDER_TYPE.OrderTypeCloseBy, 8)]
        public void Order_type_values_match_mt5(ENUM_ORDER_TYPE member, int expected)
        {
            Assert.Equal(expected, (int)member);
        }

        [Theory]
        [InlineData(ENUM_ORDER_TYPE_FILLING.OrderFillingFok, 0)]
        [InlineData(ENUM_ORDER_TYPE_FILLING.OrderFillingIoc, 1)]
        [InlineData(ENUM_ORDER_TYPE_FILLING.OrderFillingReturn, 2)]
        public void Order_filling_values_match_mt5(ENUM_ORDER_TYPE_FILLING member, int expected)
        {
            Assert.Equal(expected, (int)member);
        }

        [Theory]
        [InlineData(ENUM_ORDER_TYPE_TIME.OrderTimeGtc, 0)]
        [InlineData(ENUM_ORDER_TYPE_TIME.OrderTimeDay, 1)]
        [InlineData(ENUM_ORDER_TYPE_TIME.OrderTimeSpecified, 2)]
        [InlineData(ENUM_ORDER_TYPE_TIME.OrderTimeSpecifiedDay, 3)]
        public void Order_time_values_match_mt5(ENUM_ORDER_TYPE_TIME member, int expected)
        {
            Assert.Equal(expected, (int)member);
        }

        // --- Wire round-trip of named values (FR-003, SC-002) ---

        [Fact]
        public void Trade_request_named_values_round_trip_through_wire()
        {
            var request = new TradeRequest
            {
                Symbol = "EURUSD",
                Volume = 0.10,
                Action = ENUM_TRADE_REQUEST_ACTIONS.TradeActionDeal,
                Type = ENUM_ORDER_TYPE.OrderTypeBuy,
                TypeFilling = ENUM_ORDER_TYPE_FILLING.OrderFillingIoc,
                TypeTime = ENUM_ORDER_TYPE_TIME.OrderTimeGtc,
            };

            var parsed = TradeRequest.Parser.ParseFrom(request.ToByteArray());

            Assert.Equal(ENUM_TRADE_REQUEST_ACTIONS.TradeActionDeal, parsed.Action);
            Assert.Equal(ENUM_ORDER_TYPE.OrderTypeBuy, parsed.Type);
            Assert.Equal(ENUM_ORDER_TYPE_FILLING.OrderFillingIoc, parsed.TypeFilling);
            Assert.Equal(ENUM_ORDER_TYPE_TIME.OrderTimeGtc, parsed.TypeTime);
        }

        // --- Open-enum: unknown values preserved, never throw (FR-007, FR-008, SC-005) ---

        [Fact]
        public void Unknown_action_value_round_trips_without_loss()
        {
            // A value MT5 might add in a future build, with no named member.
            var request = new TradeRequest { Action = (ENUM_TRADE_REQUEST_ACTIONS)99 };

            var parsed = TradeRequest.Parser.ParseFrom(request.ToByteArray());

            Assert.Equal(99, (int)parsed.Action);
        }

        [Fact]
        public void Unknown_order_type_value_round_trips_without_loss()
        {
            var margin = new OrderCalcMarginRequest { Action = (ENUM_ORDER_TYPE)123 };

            var parsed = OrderCalcMarginRequest.Parser.ParseFrom(margin.ToByteArray());

            Assert.Equal(123, (int)parsed.Action);
        }

        // --- Shared order-type identity across trade and calc (FR-005, FR-015, SC-002) ---

        [Fact]
        public void Trade_type_and_calc_actions_share_the_order_type_enum()
        {
            Assert.Equal(typeof(ENUM_ORDER_TYPE), typeof(TradeRequest).GetProperty("Type")!.PropertyType);
            Assert.Equal(typeof(ENUM_ORDER_TYPE), typeof(OrderCalcMarginRequest).GetProperty("Action")!.PropertyType);
            Assert.Equal(typeof(ENUM_ORDER_TYPE), typeof(OrderCalcProfitRequest).GetProperty("Action")!.PropertyType);
        }

        [Fact]
        public void Calc_action_named_value_transmits_documented_number()
        {
            var margin = new OrderCalcMarginRequest { Action = ENUM_ORDER_TYPE.OrderTypeBuy };
            var profit = new OrderCalcProfitRequest { Action = ENUM_ORDER_TYPE.OrderTypeSell };

            Assert.Equal(0, (int)OrderCalcMarginRequest.Parser.ParseFrom(margin.ToByteArray()).Action);
            Assert.Equal(1, (int)OrderCalcProfitRequest.Parser.ParseFrom(profit.ToByteArray()).Action);
        }

        // --- Wire compatibility with the prior int32 contract (FR-006, SC-008) ---

        [Fact]
        public void Enum_field_encodes_identically_to_prior_int32()
        {
            // TradeRequest.action is field 1; MT5 value 1 (TRADE_ACTION_DEAL) encodes
            // as the varint tag/value pair (0x08, 0x01) exactly as int32 did.
            var request = new TradeRequest { Action = ENUM_TRADE_REQUEST_ACTIONS.TradeActionDeal };

            var bytes = request.ToByteArray();

            Assert.Equal(new byte[] { 0x08, 0x01 }, bytes);
        }
    }
}
