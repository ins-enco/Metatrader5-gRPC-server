using System;
using System.Threading;
using System.Threading.Tasks;

namespace MetaTrader.Grpc.Client
{
    public sealed partial class Mt5GrpcClient
    {
        public Task<TradeOperationResult> OpenOrderAsync(
            OpenOrderRequest request,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return tradeLifecycleExecutor.OpenOrderAsync(request, deadline, cancellationToken);
        }

        public Task<TradeOperationResult> ClosePositionAsync(
            ClosePositionRequest request,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return tradeLifecycleExecutor.ClosePositionAsync(request, deadline, cancellationToken);
        }

        public Task<TradeOperationResult> ModifyTradeAsync(
            ModifyTradeRequest request,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return tradeLifecycleExecutor.ModifyTradeAsync(request, deadline, cancellationToken);
        }

        public Task<TradeOperationResult> ClosePositionByAsync(
            CloseByRequest request,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return tradeLifecycleExecutor.ClosePositionByAsync(request, deadline, cancellationToken);
        }

        public Task<MultipleCloseByResult> ClosePositionsByAsync(
            ClosePositionsByRequest request,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return tradeLifecycleExecutor.ClosePositionsByAsync(request, deadline, cancellationToken);
        }
    }
}
