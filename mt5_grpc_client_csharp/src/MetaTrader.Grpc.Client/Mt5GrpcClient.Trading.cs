using System;
using System.Collections.Generic;
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

        /// <summary>
        /// Read the filtered deal history as a single response.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Large histories fail here.</b> This asks the server for the entire
        /// filtered history in one message. Measured against a live account, 1571
        /// deals serialise to about 121 KB, and a response that size terminates the
        /// server: the write aborts inside grpcio's Windows IOCP endpoint and a
        /// container set to restart goes into a restart loop. 982 deals (~75 KB)
        /// were never seen to fail, so the safe ceiling is somewhere in between and
        /// is not something this client can determine for you.
        /// </para>
        /// <para>
        /// Use <see cref="StreamDealsAsync"/> for anything large - it reads the same
        /// filters as a stream of bounded chunks - or
        /// <see cref="GetAllDealsAsync"/>, which returns this same result type but
        /// reads in chunks underneath, making migration a rename. This method's
        /// signature and behaviour are unchanged and it never falls back to either.
        /// </para>
        /// </remarks>
        /// <param name="request">The filter to read, forwarded to the server unmodified.</param>
        /// <param name="deadline">Optional call deadline, unrelated to filter times.</param>
        /// <param name="cancellationToken">Cancels the call; reported as a failure result.</param>
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

        /// <summary>
        /// Read the filtered deal history as a stream of bounded chunks - the safe
        /// surface for a large history, and the recommended one in general.
        /// </summary>
        /// <remarks>
        /// <para>
        /// One yielded <see cref="DealsResponse"/> per server message, in server
        /// order; the concatenation of every chunk's <c>Deals</c> is the whole
        /// filtered history. Consume it with <c>await foreach</c>:
        /// </para>
        /// <code>
        /// await foreach (var chunk in client.StreamDealsAsync(request))
        /// {
        ///     foreach (var deal in chunk.Deals) { /* ... */ }
        /// }
        /// </code>
        /// <para>
        /// An empty filtered history yields nothing and completes normally - it is
        /// never an error. Only one chunk is held at a time, so client memory tracks
        /// a chunk rather than the history. Use
        /// <see cref="GetAllDealsAsync"/> instead when you deliberately want the
        /// whole history materialised in one result.
        /// </para>
        /// <para>
        /// <b>Chunk size.</b> The server owns the policy: <c>chunk_size</c> unset
        /// defaults to 500 deals per message and any larger value is capped at 1000.
        /// This client transmits <see cref="DealsRequest.ChunkSize"/> exactly as you
        /// set it and supplies none of its own, so leaving it unset is what gets you
        /// the server's default. Prefer that default: at roughly 77 bytes per deal a
        /// chunk at the 1000 cap is about 77 KB, which re-enters the size band where
        /// the server was observed to abort (~75 KB survived, ~121 KB did not), so
        /// asking for a size at or near the cap gives back the failure mode this
        /// surface exists to avoid.
        /// </para>
        /// <para>
        /// <b>Failures.</b> Both a terminal in-band MT5 error and a transport fault
        /// end the enumeration by throwing <see cref="Mt5GrpcClientException"/>,
        /// whose <see cref="Mt5GrpcClientException.Error"/> carries the mapped
        /// error; no item is yielded from an error message. A server older than
        /// <c>0.4.0</c> does not implement this RPC and fails the call with status
        /// <c>Unimplemented</c> - there is <b>no</b> automatic fallback to
        /// <see cref="GetDealsAsync"/>, because that fallback would send the
        /// oversized single response that terminates the server. Cancelling
        /// <paramref name="cancellationToken"/>, exceeding
        /// <paramref name="deadline"/>, or simply <c>break</c>ing out of the
        /// <c>await foreach</c> all release the underlying call.
        /// </para>
        /// </remarks>
        /// <param name="request">
        /// The filter to read, forwarded to the server unmodified. Null reads with
        /// an empty request, which the server rejects as "No valid filter criteria
        /// provided" exactly as <see cref="GetDealsAsync"/> does.
        /// </param>
        /// <param name="deadline">Optional call deadline, unrelated to filter times.</param>
        /// <param name="cancellationToken">Cancels the call and releases it.</param>
        public IAsyncEnumerable<DealsResponse> StreamDealsAsync(
            DealsRequest? request = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return streamingInvoker.InvokeAsync(
                "TradeHistoryService.StreamDeals",
                options => TradeHistory.StreamDeals(request ?? new DealsRequest(), options),
                response => response.Error,
                deadline,
                cancellationToken);
        }

        /// <summary>
        /// Read the whole filtered deal history into one result, reading it from the
        /// server in bounded chunks. A near drop-in replacement for
        /// <see cref="GetDealsAsync"/>: same result type, so migrating is a rename.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The returned <see cref="DealsResponse"/> is synthesized by this client,
        /// not received from the server: its <c>Deals</c> is every chunk's deals
        /// appended in stream order, and its <c>Error</c> is always absent. Count and
        /// order match <see cref="GetDealsAsync"/> for the same filters.
        /// </para>
        /// <para>
        /// <b>Memory.</b> This retains the entire history in client memory for the
        /// life of the call, so cost grows with the number of deals rather than
        /// staying flat. That is the whole trade-off against
        /// <see cref="StreamDealsAsync"/>, which holds one chunk at a time -
        /// <b>prefer <see cref="StreamDealsAsync"/> for a large history</b>, and reach
        /// for this one when you genuinely need the full collection at once (a
        /// reconciliation pass, a report) and know it fits.
        /// </para>
        /// <para>
        /// <b>Failures are returned, not thrown</b>, exactly as
        /// <see cref="GetDealsAsync"/> returns them: an in-band MT5 error, a
        /// transport fault, cancellation, an elapsed deadline and an unexpected
        /// exception all come back as <c>IsSuccess == false</c> with the mapped error
        /// on <c>Error</c> and <c>Value</c> null. <b>No partial collection is ever
        /// returned</b> - deals already received before a fault are discarded rather
        /// than handed back, because a caller who inspected <c>Value</c> without
        /// checking <c>Error</c> would otherwise treat a truncated history as
        /// complete. Use <see cref="StreamDealsAsync"/> if you want to keep partial
        /// progress.
        /// </para>
        /// <para>
        /// Chunk-size handling is pass-through, identical to
        /// <see cref="StreamDealsAsync"/>: the server owns the 500 default and the
        /// 1000 cap. A server older than <c>0.4.0</c> fails the call as
        /// <c>Unimplemented</c>, with no fallback to <see cref="GetDealsAsync"/>.
        /// </para>
        /// </remarks>
        /// <param name="request">The filter to read, forwarded to the server unmodified.</param>
        /// <param name="deadline">Optional call deadline, unrelated to filter times.</param>
        /// <param name="cancellationToken">Cancels the call; reported as a failure result.</param>
        public async Task<Mt5GrpcResult<DealsResponse>> GetAllDealsAsync(
            DealsRequest? request = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            const string operation = "TradeHistoryService.StreamDeals";
            var accumulated = new DealsResponse();

            // The catch ladder mirrors Mt5GrpcUnaryInvoker's so this surface has the
            // same never-throws contract GetDealsAsync callers already rely on. The
            // streaming invoker has already logged the failure, so do not log again.
            try
            {
                await foreach (var chunk in StreamDealsAsync(request, deadline, cancellationToken)
                    .ConfigureAwait(false))
                {
                    accumulated.Deals.AddRange(chunk.Deals);
                }

                return Mt5GrpcResult<DealsResponse>.Success(accumulated);
            }
            catch (Mt5GrpcClientException exception)
            {
                var error = exception.Error ?? Mt5GrpcErrorMapper.FromException(operation, exception);
                return Mt5GrpcResult<DealsResponse>.Failure(error);
            }
            catch (OperationCanceledException exception)
            {
                return Mt5GrpcResult<DealsResponse>.Failure(
                    Mt5GrpcErrorMapper.FromCancellation(operation, exception));
            }
            catch (Exception exception)
            {
                return Mt5GrpcResult<DealsResponse>.Failure(
                    Mt5GrpcErrorMapper.FromException(operation, exception));
            }
        }
    }
}
