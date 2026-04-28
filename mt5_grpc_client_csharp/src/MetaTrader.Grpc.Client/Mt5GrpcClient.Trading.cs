using System;
using System.Threading;
using System.Threading.Tasks;
using Metatrader.V1;

namespace MetaTrader.Grpc.Client
{
    public sealed partial class Mt5GrpcClient
    {
        public Task<Mt5GrpcResult<OrdersGetResponse>> GetOrdersAsync(
            OrdersGetRequest? request = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return invoker.InvokeAsync(
                "OrdersService.GetOrders",
                options => Orders.GetOrdersAsync(request ?? new OrdersGetRequest(), options),
                response => response.Error,
                deadline,
                cancellationToken);
        }

        public Task<Mt5GrpcResult<OrdersTotalResponse>> GetOrdersTotalAsync(
            OrdersTotalRequest? request = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return invoker.InvokeAsync(
                "OrdersService.GetOrdersTotal",
                options => Orders.GetOrdersTotalAsync(request ?? new OrdersTotalRequest(), options),
                response => response.Error,
                deadline,
                cancellationToken);
        }

        public Task<Mt5GrpcResult<OrderCheckResponse>> CheckOrderAsync(
            OrderCheckRequest? request = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return invoker.InvokeAsync(
                "OrderCheckService.CheckOrder",
                options => OrderCheck.CheckOrderAsync(request ?? new OrderCheckRequest(), options),
                response => response.Error,
                deadline,
                cancellationToken);
        }

        public Task<Mt5GrpcResult<OrderCalcMarginResponse>> CalcMarginAsync(
            OrderCalcMarginRequest? request = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return invoker.InvokeAsync(
                "OrderCalcService.CalcMargin",
                options => OrderCalc.CalcMarginAsync(request ?? new OrderCalcMarginRequest(), options),
                response => response.Error,
                deadline,
                cancellationToken);
        }

        public Task<Mt5GrpcResult<OrderCalcProfitResponse>> CalcProfitAsync(
            OrderCalcProfitRequest? request = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return invoker.InvokeAsync(
                "OrderCalcService.CalcProfit",
                options => OrderCalc.CalcProfitAsync(request ?? new OrderCalcProfitRequest(), options),
                response => response.Error,
                deadline,
                cancellationToken);
        }

        public Task<Mt5GrpcResult<OrderSendResponse>> SendOrderAsync(
            OrderSendRequest? request = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return invoker.InvokeAsync(
                "OrderSendService.SendOrder",
                options => OrderSend.SendOrderAsync(request ?? new OrderSendRequest(), options),
                response => response.Error,
                deadline,
                cancellationToken);
        }

        public Task<Mt5GrpcResult<PositionsGetResponse>> GetPositionsAsync(
            PositionsGetRequest? request = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return invoker.InvokeAsync(
                "PositionsService.GetPositions",
                options => Positions.GetPositionsAsync(request ?? new PositionsGetRequest(), options),
                response => response.Error,
                deadline,
                cancellationToken);
        }

        public Task<Mt5GrpcResult<PositionsTotalResponse>> GetPositionsTotalAsync(
            PositionsTotalRequest? request = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return invoker.InvokeAsync(
                "PositionsService.GetPositionsTotal",
                options => Positions.GetPositionsTotalAsync(request ?? new PositionsTotalRequest(), options),
                response => response.Error,
                deadline,
                cancellationToken);
        }

        public Task<Mt5GrpcResult<HistoryOrdersResponse>> GetHistoryOrdersAsync(
            HistoryOrdersRequest? request = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return invoker.InvokeAsync(
                "HistoryOrdersService.GetHistoryOrders",
                options => HistoryOrders.GetHistoryOrdersAsync(request ?? new HistoryOrdersRequest(), options),
                response => response.Error,
                deadline,
                cancellationToken);
        }

        public Task<Mt5GrpcResult<HistoryOrdersTotalResponse>> GetHistoryOrdersTotalAsync(
            HistoryOrdersTotalRequest? request = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return invoker.InvokeAsync(
                "HistoryOrdersService.GetHistoryOrdersTotal",
                options => HistoryOrders.GetHistoryOrdersTotalAsync(request ?? new HistoryOrdersTotalRequest(), options),
                response => response.Error,
                deadline,
                cancellationToken);
        }

        public Task<Mt5GrpcResult<DealsResponse>> GetDealsAsync(
            DealsRequest? request = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return invoker.InvokeAsync(
                "TradeHistoryService.GetDeals",
                options => TradeHistory.GetDealsAsync(request ?? new DealsRequest(), options),
                response => response.Error,
                deadline,
                cancellationToken);
        }
    }
}
