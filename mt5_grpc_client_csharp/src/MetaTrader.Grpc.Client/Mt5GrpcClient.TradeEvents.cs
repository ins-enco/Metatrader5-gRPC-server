using System;
using System.Collections.Generic;
using System.Threading;
using Metatrader.V1;

namespace MetaTrader.Grpc.Client
{
    public sealed partial class Mt5GrpcClient
    {
        /// <summary>
        /// Subscribe to live trade transaction events for the connected account.
        ///
        /// This is the primary surface: an <see cref="IAsyncEnumerable{T}"/> mapping
        /// 1:1 to the server stream. Enumerate it with <c>await foreach</c>; each
        /// item is one newly added deal, delivered exactly once in chronological
        /// order. The stream ends when the <paramref name="cancellationToken"/> is
        /// cancelled or the deadline elapses. A terminal MT5 failure surfaces as an
        /// <see cref="Mt5GrpcClientException"/> whose <see cref="Mt5GrpcClientException.Error"/>
        /// carries the mapped error.
        /// </summary>
        public IAsyncEnumerable<TradeTransactionEvent> SubscribeTradeTransactionsAsync(
            SubscribeTradeTransactionsRequest? request = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default)
        {
            return streamingInvoker.InvokeAsync(
                "TradeEventsService.SubscribeTradeTransactions",
                options => TradeEvents.SubscribeTradeTransactions(
                    request ?? new SubscribeTradeTransactionsRequest(), options),
                evt => evt.Error,
                deadline,
                cancellationToken);
        }

        /// <summary>
        /// Convenience surface: create an event-style subscription over the async
        /// sequence. Attach handlers to <see cref="TradeTransactionSubscription.TransactionReceived"/>,
        /// <see cref="TradeTransactionSubscription.Completed"/>, and
        /// <see cref="TradeTransactionSubscription.Faulted"/>, then call
        /// <see cref="TradeTransactionSubscription.Start"/> (or await
        /// <see cref="TradeTransactionSubscription.RunAsync"/>).
        /// </summary>
        public TradeTransactionSubscription SubscribeTradeTransactions(
            SubscribeTradeTransactionsRequest? request = null,
            DateTime? deadline = null)
        {
            return new TradeTransactionSubscription(
                token => SubscribeTradeTransactionsAsync(request, deadline, token));
        }
    }
}
