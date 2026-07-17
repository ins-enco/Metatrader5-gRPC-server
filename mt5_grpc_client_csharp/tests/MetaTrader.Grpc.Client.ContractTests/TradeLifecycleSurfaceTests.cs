using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MetaTrader.Grpc.Client;
using Metatrader.V1;
using Xunit;

namespace MetaTrader.Grpc.Client.ContractTests
{
    public sealed class TradeLifecycleSurfaceTests
    {
        [Fact]
        public void Open_method_has_the_supported_async_signature()
        {
            var method = typeof(Mt5GrpcClient).GetMethod("OpenOrderAsync", BindingFlags.Public | BindingFlags.Instance);

            Assert.NotNull(method);
            Assert.Equal(typeof(Task<TradeOperationResult>), method!.ReturnType);
            var parameters = method.GetParameters();
            Assert.Equal(new[] { typeof(OpenOrderRequest), typeof(DateTime?), typeof(CancellationToken) }, parameters.Select(p => p.ParameterType));
            Assert.False(parameters[0].IsOptional);
            Assert.True(parameters[1].IsOptional);
            Assert.Null(parameters[1].DefaultValue);
            Assert.True(parameters[2].IsOptional);
        }

        [Fact]
        public void Close_methods_require_only_the_target_ticket_and_optional_volume()
        {
            var closePosition = typeof(Mt5GrpcClient).GetMethod(
                "ClosePositionAsync",
                BindingFlags.Public | BindingFlags.Instance);
            var closeOrder = typeof(Mt5GrpcClient).GetMethod(
                "CloseOrderAsync",
                BindingFlags.Public | BindingFlags.Instance);

            Assert.NotNull(closePosition);
            Assert.Equal(typeof(Task<TradeOperationResult>), closePosition!.ReturnType);
            var positionParameters = closePosition.GetParameters();
            Assert.Equal(
                new[] { typeof(long), typeof(double?), typeof(DateTime?), typeof(CancellationToken) },
                positionParameters.Select(parameter => parameter.ParameterType));
            Assert.False(positionParameters[0].IsOptional);
            Assert.True(positionParameters[1].IsOptional);
            Assert.Null(positionParameters[1].DefaultValue);
            Assert.True(positionParameters[2].IsOptional);
            Assert.Null(positionParameters[2].DefaultValue);
            Assert.True(positionParameters[3].IsOptional);

            Assert.NotNull(closeOrder);
            Assert.Equal(typeof(Task<TradeOperationResult>), closeOrder!.ReturnType);
            var orderParameters = closeOrder.GetParameters();
            Assert.Equal(
                new[] { typeof(long), typeof(DateTime?), typeof(CancellationToken) },
                orderParameters.Select(parameter => parameter.ParameterType));
            Assert.False(orderParameters[0].IsOptional);
            Assert.True(orderParameters[1].IsOptional);
            Assert.Null(orderParameters[1].DefaultValue);
            Assert.True(orderParameters[2].IsOptional);

            Assert.Contains(TradeLifecycleOperation.CloseOrder, Enum.GetValues(typeof(TradeLifecycleOperation)).Cast<TradeLifecycleOperation>());
        }

        [Fact]
        public void Open_request_requires_intent_fields_and_omits_raw_action()
        {
            Assert.NotNull(typeof(OpenOrderRequest).GetConstructor(new[] { typeof(string), typeof(ENUM_ORDER_TYPE), typeof(double) }));
            Assert.Null(typeof(OpenOrderRequest).GetProperty("Action"));
            Assert.Null(typeof(Mt5GrpcClient).Assembly.GetType("MetaTrader.Grpc.Client.ClosePositionRequest"));
            Assert.Null(typeof(Mt5GrpcClient).Assembly.GetType("MetaTrader.Grpc.Client.PositionSide"));
        }

        [Fact]
        public void Trade_operation_result_preserves_the_existing_call_result_surface()
        {
            Assert.Equal(typeof(TradeLifecycleOperation), typeof(TradeOperationResult).GetProperty("Operation")!.PropertyType);
            Assert.Equal(typeof(Mt5GrpcResult<OrderSendResponse>), typeof(TradeOperationResult).GetProperty("CallResult")!.PropertyType);
            Assert.Equal(typeof(TradeExecutionStatus?), typeof(TradeOperationResult).GetProperty("ExecutionStatus")!.PropertyType);
            Assert.Equal(typeof(int?), typeof(TradeOperationResult).GetProperty("RawRetcode")!.PropertyType);
        }

        [Fact]
        public void Modify_method_and_union_types_have_the_supported_surface()
        {
            var method = typeof(Mt5GrpcClient).GetMethod("ModifyTradeAsync", BindingFlags.Public | BindingFlags.Instance);

            Assert.NotNull(method);
            Assert.Equal(typeof(Task<TradeOperationResult>), method!.ReturnType);
            Assert.Equal(
                new[] { typeof(ModifyTradeRequest), typeof(DateTime?), typeof(CancellationToken) },
                method.GetParameters().Select(p => p.ParameterType));
            Assert.NotNull(typeof(PositionModification).GetConstructor(new[] { typeof(long), typeof(double), typeof(double) }));
            Assert.NotNull(typeof(PendingOrderModification).GetConstructors().Single());
            Assert.NotNull(typeof(ModifyTradeRequest).GetConstructor(new[] { typeof(PositionModification) }));
            Assert.NotNull(typeof(ModifyTradeRequest).GetConstructor(new[] { typeof(PendingOrderModification) }));
            Assert.Contains(TradeLifecycleOperation.ModifyPosition, Enum.GetValues(typeof(TradeLifecycleOperation)).Cast<TradeLifecycleOperation>());
            Assert.Contains(TradeLifecycleOperation.ModifyPendingOrder, Enum.GetValues(typeof(TradeLifecycleOperation)).Cast<TradeLifecycleOperation>());
        }

        [Fact]
        public void Close_by_method_and_request_preserve_caller_ticket_roles()
        {
            var method = typeof(Mt5GrpcClient).GetMethod("ClosePositionByAsync", BindingFlags.Public | BindingFlags.Instance);

            Assert.NotNull(method);
            Assert.Equal(typeof(Task<TradeOperationResult>), method!.ReturnType);
            Assert.Equal(
                new[] { typeof(CloseByRequest), typeof(DateTime?), typeof(CancellationToken) },
                method.GetParameters().Select(p => p.ParameterType));

            var request = new CloseByRequest(11, 22) { Magic = 33, Comment = "audit" };
            Assert.Equal(11, request.PositionTicket);
            Assert.Equal(22, request.OppositePositionTicket);
            Assert.Equal(33, request.Magic);
            Assert.Equal("audit", request.Comment);
        }

        [Fact]
        public void Multiple_close_by_method_and_request_have_the_supported_surface()
        {
            var method = typeof(Mt5GrpcClient).GetMethod("ClosePositionsByAsync", BindingFlags.Public | BindingFlags.Instance);

            Assert.NotNull(method);
            Assert.Equal(typeof(Task<MultipleCloseByResult>), method!.ReturnType);
            Assert.Equal(
                new[] { typeof(ClosePositionsByRequest), typeof(DateTime?), typeof(CancellationToken) },
                method.GetParameters().Select(p => p.ParameterType));

            var request = new ClosePositionsByRequest("EURUSD") { Magic = 42, Comment = "batch" };
            Assert.Equal("EURUSD", request.Symbol);
            Assert.Equal(42, request.Magic);
            Assert.Equal("batch", request.Comment);
        }

        [Fact]
        public void Multiple_close_by_results_copy_collections_and_preserve_pair_roles()
        {
            var tickets = new List<long> { 1, 2 };
            var pairs = new List<CloseByPairOutcome>
            {
                new CloseByPairOutcome(1, 1, 2, PairAttemptState.Unattempted, null)
            };
            var remainders = new List<PositionRemainder>
            {
                new PositionRemainder(3, 0.25, PositionRemainderReason.NoOpposite)
            };
            var error = new Mt5GrpcError { Operation = "batch", Message = "stopped" };

            var result = new MultipleCloseByResult(
                MultipleCloseByStatus.Cancelled,
                error,
                tickets,
                pairs,
                remainders);
            tickets.Add(4);
            pairs.Clear();
            remainders.Clear();

            Assert.Equal(MultipleCloseByStatus.Cancelled, result.Status);
            Assert.Same(error, result.BatchError);
            Assert.Equal(new long[] { 1, 2 }, result.FrozenTickets);
            var pair = Assert.Single(result.Pairs);
            Assert.Equal(1, pair.PairIndex);
            Assert.Equal(1, pair.PositionTicket);
            Assert.Equal(2, pair.OppositePositionTicket);
            Assert.Equal(PairAttemptState.Unattempted, pair.AttemptState);
            Assert.Null(pair.OperationResult);
            Assert.Equal(PositionRemainderReason.NoOpposite, Assert.Single(result.Remainders).Reason);
        }

        [Fact]
        public void Batch_status_and_remainder_enums_expose_every_contract_category()
        {
            Assert.Equal(
                new[]
                {
                    MultipleCloseByStatus.Completed,
                    MultipleCloseByStatus.ValidationFailed,
                    MultipleCloseByStatus.DiscoveryFailed,
                    MultipleCloseByStatus.RefreshFailed,
                    MultipleCloseByStatus.Cancelled,
                    MultipleCloseByStatus.DeadlineExceeded
                },
                Enum.GetValues(typeof(MultipleCloseByStatus)).Cast<MultipleCloseByStatus>());
            Assert.Equal(
                new[]
                {
                    PositionRemainderReason.NoOpposite,
                    PositionRemainderReason.BecameIneligible,
                    PositionRemainderReason.WithheldAfterPair,
                    PositionRemainderReason.UnattemptedAfterStop,
                    PositionRemainderReason.MissingFromRefresh,
                    PositionRemainderReason.InvalidSnapshot
                },
                Enum.GetValues(typeof(PositionRemainderReason)).Cast<PositionRemainderReason>());
        }
    }
}
