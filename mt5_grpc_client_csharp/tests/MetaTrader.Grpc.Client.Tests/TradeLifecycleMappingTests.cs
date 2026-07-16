using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Metatrader.V1;
using Xunit;

namespace MetaTrader.Grpc.Client.Tests
{
    public sealed class TradeLifecycleMappingTests
    {
        [Fact]
        public async Task Market_open_maps_every_applicable_field_and_sends_once_without_lookup()
        {
            var harness = new ExecutorHarness();
            var request = new OpenOrderRequest("EURUSD", ENUM_ORDER_TYPE.OrderTypeBuy, 0.25)
            {
                Price = 1.1,
                StopLoss = 1.0,
                TakeProfit = 1.2,
                Deviation = 8,
                FillingPolicy = ENUM_ORDER_TYPE_FILLING.OrderFillingIoc,
                TimePolicy = ENUM_ORDER_TYPE_TIME.OrderTimeGtc,
                Magic = 42,
                Comment = "market-open"
            };
            var deadline = DateTime.UtcNow.AddSeconds(10);
            using var cancellation = new CancellationTokenSource();

            var result = await harness.Executor.OpenOrderAsync(request, deadline, cancellation.Token);

            Assert.True(result.CallResult.IsSuccess);
            Assert.Equal(TradeLifecycleOperation.Open, result.Operation);
            Assert.Equal(TradeExecutionStatus.Completed, result.ExecutionStatus);
            Assert.Equal(10009, result.RawRetcode);
            Assert.Equal(1, harness.SendCalls);
            Assert.Equal(0, harness.PositionCalls);
            Assert.Equal(deadline, harness.LastDeadline);
            Assert.Equal(cancellation.Token, harness.LastCancellationToken);

            var mapped = Assert.IsType<TradeRequest>(harness.LastSendRequest!.TradeRequest);
            Assert.Equal(ENUM_TRADE_REQUEST_ACTIONS.TradeActionDeal, mapped.Action);
            Assert.Equal("EURUSD", mapped.Symbol);
            Assert.Equal(ENUM_ORDER_TYPE.OrderTypeBuy, mapped.Type);
            Assert.Equal(0.25, mapped.Volume);
            Assert.Equal(1.1, mapped.Price);
            Assert.Equal(1.0, mapped.Sl);
            Assert.Equal(1.2, mapped.Tp);
            Assert.Equal(8, mapped.Deviation);
            Assert.Equal(ENUM_ORDER_TYPE_FILLING.OrderFillingIoc, mapped.TypeFilling);
            Assert.Equal(ENUM_ORDER_TYPE_TIME.OrderTimeGtc, mapped.TypeTime);
            Assert.Equal(42, mapped.Magic);
            Assert.Equal("market-open", mapped.Comment);
            Assert.False(mapped.HasPosition);
            Assert.False(mapped.HasOrder);
        }

        [Fact]
        public async Task Pending_open_clones_expiration_and_preserves_caller_snapshot()
        {
            var harness = new ExecutorHarness();
            var expiration = Timestamp.FromDateTime(DateTime.SpecifyKind(
                new DateTime(2030, 1, 2, 3, 4, 5), DateTimeKind.Utc));
            var request = new OpenOrderRequest("EURUSD", ENUM_ORDER_TYPE.OrderTypeBuyStopLimit, 0.5)
            {
                Price = 1.2,
                StopLimitPrice = 1.19,
                StopLoss = 1.1,
                TakeProfit = 1.3,
                FillingPolicy = ENUM_ORDER_TYPE_FILLING.OrderFillingReturn,
                TimePolicy = ENUM_ORDER_TYPE_TIME.OrderTimeSpecified,
                Expiration = expiration,
                Magic = 7,
                Comment = "pending-open"
            };
            var originalSeconds = expiration.Seconds;

            await harness.Executor.OpenOrderAsync(request, null, CancellationToken.None);

            var mapped = harness.LastSendRequest!.TradeRequest;
            Assert.Equal(ENUM_TRADE_REQUEST_ACTIONS.TradeActionPending, mapped.Action);
            Assert.Equal(1.2, mapped.Price);
            Assert.Equal(1.19, mapped.Stoplimit);
            Assert.NotSame(expiration, mapped.Expiration);
            Assert.Equal(expiration, mapped.Expiration);
            Assert.Equal(originalSeconds, expiration.Seconds);
            Assert.Same(expiration, request.Expiration);

            mapped.Expiration.Seconds++;
            Assert.Equal(originalSeconds, expiration.Seconds);
        }

        [Theory]
        [MemberData(nameof(InvalidOpenRequests))]
        public async Task Invalid_open_input_returns_actionable_failure_and_makes_zero_calls(OpenOrderRequest? request)
        {
            var harness = new ExecutorHarness();

            var result = await harness.Executor.OpenOrderAsync(request, null, CancellationToken.None);

            Assert.False(result.CallResult.IsSuccess);
            Assert.False(string.IsNullOrWhiteSpace(result.CallResult.Error!.Message));
            Assert.Null(result.ExecutionStatus);
            Assert.Equal(0, harness.SendCalls);
            Assert.Equal(0, harness.PositionCalls);
        }

        [Fact]
        public async Task Full_close_maps_snapshot_volume_and_opposite_side_without_lookup()
        {
            var harness = new ExecutorHarness();
            var request = new ClosePositionRequest(101, "GBPUSD", PositionSide.Buy, 1.25)
            {
                Price = 1.27,
                Deviation = 3,
                FillingPolicy = ENUM_ORDER_TYPE_FILLING.OrderFillingFok,
                Magic = 8,
                Comment = "full-close"
            };

            var result = await harness.Executor.ClosePositionAsync(request, null, CancellationToken.None);

            Assert.Equal(TradeLifecycleOperation.Close, result.Operation);
            Assert.Equal(1, harness.SendCalls);
            Assert.Equal(0, harness.PositionCalls);
            var mapped = harness.LastSendRequest!.TradeRequest;
            Assert.Equal(ENUM_TRADE_REQUEST_ACTIONS.TradeActionDeal, mapped.Action);
            Assert.Equal(101, mapped.Position);
            Assert.Equal("GBPUSD", mapped.Symbol);
            Assert.Equal(ENUM_ORDER_TYPE.OrderTypeSell, mapped.Type);
            Assert.Equal(1.25, mapped.Volume);
            Assert.Equal(1.27, mapped.Price);
            Assert.Equal("full-close", mapped.Comment);
        }

        [Fact]
        public async Task Partial_close_maps_requested_volume_and_sell_position_to_buy_order()
        {
            var harness = new ExecutorHarness();
            var request = new ClosePositionRequest(202, "USDJPY", PositionSide.Sell, 2.0)
            {
                Volume = 0.75
            };

            await harness.Executor.ClosePositionAsync(request, null, CancellationToken.None);

            var mapped = harness.LastSendRequest!.TradeRequest;
            Assert.Equal(ENUM_ORDER_TYPE.OrderTypeBuy, mapped.Type);
            Assert.Equal(0.75, mapped.Volume);
        }

        [Theory]
        [MemberData(nameof(InvalidCloseRequests))]
        public async Task Invalid_close_input_makes_zero_calls(ClosePositionRequest? request)
        {
            var harness = new ExecutorHarness();

            var result = await harness.Executor.ClosePositionAsync(request, null, CancellationToken.None);

            Assert.False(result.CallResult.IsSuccess);
            Assert.Null(result.ExecutionStatus);
            Assert.Equal(0, harness.SendCalls);
            Assert.Equal(0, harness.PositionCalls);
        }

        [Fact]
        public async Task Transport_failure_is_returned_after_one_send_without_retry()
        {
            var harness = new ExecutorHarness
            {
                SendResult = Mt5GrpcResult<OrderSendResponse>.Failure(new Mt5GrpcError
                {
                    Operation = "OrderSendService.SendOrder",
                    StatusCode = StatusCode.Unavailable,
                    Message = "uncertain"
                })
            };

            var result = await harness.Executor.OpenOrderAsync(
                new OpenOrderRequest("EURUSD", ENUM_ORDER_TYPE.OrderTypeBuy, 0.1),
                null,
                CancellationToken.None);

            Assert.False(result.CallResult.IsSuccess);
            Assert.Null(result.ExecutionStatus);
            Assert.Equal(1, harness.SendCalls);
            Assert.Equal(0, harness.PositionCalls);
        }

        [Fact]
        public async Task Position_modification_maps_final_protection_state_and_clear_values()
        {
            var harness = new ExecutorHarness();
            var request = new ModifyTradeRequest(new PositionModification(301, 0, 1.35));

            var result = await harness.Executor.ModifyTradeAsync(request, null, CancellationToken.None);

            Assert.Equal(TradeLifecycleOperation.ModifyPosition, result.Operation);
            Assert.Equal(1, harness.SendCalls);
            Assert.Equal(0, harness.PositionCalls);
            var mapped = harness.LastSendRequest!.TradeRequest;
            Assert.Equal(ENUM_TRADE_REQUEST_ACTIONS.TradeActionSltp, mapped.Action);
            Assert.Equal(301, mapped.Position);
            Assert.False(mapped.HasOrder);
            Assert.True(mapped.HasSl);
            Assert.Equal(0, mapped.Sl);
            Assert.Equal(1.35, mapped.Tp);
        }

        [Fact]
        public async Task Pending_modification_maps_final_state_and_clones_expiration()
        {
            var harness = new ExecutorHarness();
            var expiration = Timestamp.FromDateTime(DateTime.SpecifyKind(
                new DateTime(2031, 2, 3, 4, 5, 6), DateTimeKind.Utc));
            var pending = new PendingOrderModification(
                401,
                1.2,
                1.19,
                1.1,
                1.3,
                ENUM_ORDER_TYPE_TIME.OrderTimeSpecified)
            {
                Expiration = expiration
            };

            var result = await harness.Executor.ModifyTradeAsync(
                new ModifyTradeRequest(pending),
                null,
                CancellationToken.None);

            Assert.Equal(TradeLifecycleOperation.ModifyPendingOrder, result.Operation);
            Assert.Equal(1, harness.SendCalls);
            Assert.Equal(0, harness.PositionCalls);
            var mapped = harness.LastSendRequest!.TradeRequest;
            Assert.Equal(ENUM_TRADE_REQUEST_ACTIONS.TradeActionModify, mapped.Action);
            Assert.Equal(401, mapped.Order);
            Assert.False(mapped.HasPosition);
            Assert.Equal(1.2, mapped.Price);
            Assert.Equal(1.19, mapped.Stoplimit);
            Assert.Equal(1.1, mapped.Sl);
            Assert.Equal(1.3, mapped.Tp);
            Assert.Equal(ENUM_ORDER_TYPE_TIME.OrderTimeSpecified, mapped.TypeTime);
            Assert.NotSame(expiration, mapped.Expiration);
            Assert.Equal(expiration, mapped.Expiration);
            mapped.Expiration.Seconds++;
            Assert.NotEqual(mapped.Expiration.Seconds, expiration.Seconds);
            Assert.Same(expiration, pending.Expiration);
        }

        [Theory]
        [MemberData(nameof(InvalidModificationRequests))]
        public async Task Invalid_or_ambiguous_modification_makes_zero_calls(ModifyTradeRequest? request)
        {
            var harness = new ExecutorHarness();

            var result = await harness.Executor.ModifyTradeAsync(request, null, CancellationToken.None);

            Assert.False(result.CallResult.IsSuccess);
            Assert.Null(result.ExecutionStatus);
            Assert.Equal(0, harness.SendCalls);
            Assert.Equal(0, harness.PositionCalls);
        }

        [Fact]
        public async Task Close_by_maps_unswapped_ticket_roles_and_optional_values_once()
        {
            var harness = new ExecutorHarness();
            var request = new CloseByRequest(501, 502)
            {
                Magic = 88,
                Comment = "single-close-by"
            };

            var result = await harness.Executor.ClosePositionByAsync(request, null, CancellationToken.None);

            Assert.Equal(TradeLifecycleOperation.CloseBy, result.Operation);
            Assert.True(result.CallResult.IsSuccess);
            Assert.Equal(TradeExecutionStatus.Completed, result.ExecutionStatus);
            Assert.Equal(1, harness.SendCalls);
            Assert.Equal(0, harness.PositionCalls);
            var mapped = harness.LastSendRequest!.TradeRequest;
            Assert.Equal(ENUM_TRADE_REQUEST_ACTIONS.TradeActionCloseBy, mapped.Action);
            Assert.Equal(501, mapped.Position);
            Assert.Equal(502, mapped.PositionBy);
            Assert.Equal(88, mapped.Magic);
            Assert.Equal("single-close-by", mapped.Comment);
        }

        [Theory]
        [InlineData(0, 2)]
        [InlineData(1, 0)]
        [InlineData(-1, 2)]
        [InlineData(2, 2)]
        public async Task Invalid_close_by_tickets_make_zero_calls(long position, long opposite)
        {
            var harness = new ExecutorHarness();

            var result = await harness.Executor.ClosePositionByAsync(
                new CloseByRequest(position, opposite),
                null,
                CancellationToken.None);

            Assert.False(result.CallResult.IsSuccess);
            Assert.Equal(0, harness.SendCalls);
            Assert.Equal(0, harness.PositionCalls);
        }

        [Fact]
        public async Task Null_close_by_request_makes_zero_calls()
        {
            var harness = new ExecutorHarness();

            var result = await harness.Executor.ClosePositionByAsync(null, null, CancellationToken.None);

            Assert.False(result.CallResult.IsSuccess);
            Assert.Equal(0, harness.SendCalls);
            Assert.Equal(0, harness.PositionCalls);
        }

        [Fact]
        public async Task Mt5_close_by_rejection_is_received_and_preserved_without_retry()
        {
            var response = new OrderSendResponse
            {
                TradeResult = new TradeResult
                {
                    Retcode = 10006,
                    Comment = "rejected by MT5",
                    RequestId = 999
                }
            };
            var harness = new ExecutorHarness
            {
                SendResult = Mt5GrpcResult<OrderSendResponse>.Success(response)
            };

            var result = await harness.Executor.ClosePositionByAsync(
                new CloseByRequest(601, 602),
                null,
                CancellationToken.None);

            Assert.True(result.CallResult.IsSuccess);
            Assert.Same(response, result.CallResult.Value);
            Assert.Equal(TradeExecutionStatus.RejectedOrFailed, result.ExecutionStatus);
            Assert.Equal(10006, result.RawRetcode);
            Assert.Equal(1, harness.SendCalls);
            Assert.Equal(0, harness.PositionCalls);
        }

        public static IEnumerable<object?[]> InvalidOpenRequests()
        {
            yield return new object?[] { null };
            yield return new object?[] { new OpenOrderRequest(" ", ENUM_ORDER_TYPE.OrderTypeBuy, 1) };
            yield return new object?[] { new OpenOrderRequest("EURUSD", ENUM_ORDER_TYPE.OrderTypeCloseBy, 1) };
            yield return new object?[] { new OpenOrderRequest("EURUSD", (ENUM_ORDER_TYPE)99, 1) };
            yield return new object?[] { new OpenOrderRequest("EURUSD", ENUM_ORDER_TYPE.OrderTypeBuy, 0) };
            yield return new object?[] { new OpenOrderRequest("EURUSD", ENUM_ORDER_TYPE.OrderTypeBuy, double.NaN) };
            yield return new object?[] { new OpenOrderRequest("EURUSD", ENUM_ORDER_TYPE.OrderTypeBuy, 1) { Price = double.PositiveInfinity } };
            yield return new object?[] { new OpenOrderRequest("EURUSD", ENUM_ORDER_TYPE.OrderTypeBuyStopLimit, 1) { Price = 1.1 } };
            yield return new object?[]
            {
                new OpenOrderRequest("EURUSD", ENUM_ORDER_TYPE.OrderTypeBuyLimit, 1)
                {
                    Price = 1.1,
                    TimePolicy = ENUM_ORDER_TYPE_TIME.OrderTimeSpecified
                }
            };
            yield return new object?[]
            {
                new OpenOrderRequest("EURUSD", ENUM_ORDER_TYPE.OrderTypeBuy, 1)
                {
                    TimePolicy = ENUM_ORDER_TYPE_TIME.OrderTimeGtc,
                    Expiration = Timestamp.FromDateTime(DateTime.UtcNow.AddDays(1))
                }
            };
        }

        public static IEnumerable<object?[]> InvalidCloseRequests()
        {
            yield return new object?[] { null };
            yield return new object?[] { new ClosePositionRequest(0, "EURUSD", PositionSide.Buy, 1) };
            yield return new object?[] { new ClosePositionRequest(1, " ", PositionSide.Buy, 1) };
            yield return new object?[] { new ClosePositionRequest(1, "EURUSD", (PositionSide)99, 1) };
            yield return new object?[] { new ClosePositionRequest(1, "EURUSD", PositionSide.Buy, 0) };
            yield return new object?[] { new ClosePositionRequest(1, "EURUSD", PositionSide.Buy, double.NaN) };
            yield return new object?[] { new ClosePositionRequest(1, "EURUSD", PositionSide.Buy, 1) { Volume = 0 } };
            yield return new object?[] { new ClosePositionRequest(1, "EURUSD", PositionSide.Buy, 1) { Volume = 2 } };
            yield return new object?[] { new ClosePositionRequest(1, "EURUSD", PositionSide.Buy, 1) { Price = double.NegativeInfinity } };
        }

        public static IEnumerable<object?[]> InvalidModificationRequests()
        {
            yield return new object?[] { null };
            yield return new object?[] { new ModifyTradeRequest(null, null) };
            yield return new object?[]
            {
                new ModifyTradeRequest(
                    new PositionModification(1, 1, 1),
                    new PendingOrderModification(2, 1, 0, 1, 1, ENUM_ORDER_TYPE_TIME.OrderTimeGtc))
            };
            yield return new object?[] { new ModifyTradeRequest(new PositionModification(0, 1, 1)) };
            yield return new object?[] { new ModifyTradeRequest(new PositionModification(1, double.NaN, 1)) };
            yield return new object?[]
            {
                new ModifyTradeRequest(new PendingOrderModification(
                    0, 1, 0, 1, 1, ENUM_ORDER_TYPE_TIME.OrderTimeGtc))
            };
            yield return new object?[]
            {
                new ModifyTradeRequest(new PendingOrderModification(
                    1, double.PositiveInfinity, 0, 1, 1, ENUM_ORDER_TYPE_TIME.OrderTimeGtc))
            };
            yield return new object?[]
            {
                new ModifyTradeRequest(new PendingOrderModification(
                    1, 1, 0, 1, 1, ENUM_ORDER_TYPE_TIME.OrderTimeSpecified))
            };
            yield return new object?[]
            {
                new ModifyTradeRequest(new PendingOrderModification(
                    1, 1, 0, 1, 1, ENUM_ORDER_TYPE_TIME.OrderTimeGtc)
                {
                    Expiration = Timestamp.FromDateTime(DateTime.UtcNow.AddDays(1))
                })
            };
        }

        private sealed class ExecutorHarness
        {
            public ExecutorHarness()
            {
                Executor = new TradeLifecycleExecutor(SendAsync, GetPositionsAsync);
            }

            public TradeLifecycleExecutor Executor { get; }
            public int SendCalls { get; private set; }
            public int PositionCalls { get; private set; }
            public OrderSendRequest? LastSendRequest { get; private set; }
            public DateTime? LastDeadline { get; private set; }
            public CancellationToken LastCancellationToken { get; private set; }
            public Mt5GrpcResult<OrderSendResponse> SendResult { get; set; } =
                Mt5GrpcResult<OrderSendResponse>.Success(new OrderSendResponse
                {
                    TradeResult = new TradeResult { Retcode = 10009 }
                });

            private Task<Mt5GrpcResult<OrderSendResponse>> SendAsync(
                OrderSendRequest request,
                DateTime? deadline,
                CancellationToken cancellationToken)
            {
                SendCalls++;
                LastSendRequest = request;
                LastDeadline = deadline;
                LastCancellationToken = cancellationToken;
                return Task.FromResult(SendResult);
            }

            private Task<Mt5GrpcResult<PositionsGetResponse>> GetPositionsAsync(
                PositionsGetRequest request,
                DateTime? deadline,
                CancellationToken cancellationToken)
            {
                PositionCalls++;
                return Task.FromResult(Mt5GrpcResult<PositionsGetResponse>.Success(new PositionsGetResponse()));
            }
        }
    }
}
