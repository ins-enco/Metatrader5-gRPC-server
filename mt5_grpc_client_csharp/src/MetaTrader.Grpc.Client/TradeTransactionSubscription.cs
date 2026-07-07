using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Metatrader.V1;

namespace MetaTrader.Grpc.Client
{
    /// <summary>
    /// A C# <c>event</c>-style wrapper layered over the
    /// <see cref="IAsyncEnumerable{T}"/> trade-transaction stream (the convenience
    /// surface of FR-011). It drives the async sequence on a background task and
    /// raises <see cref="TransactionReceived"/> for each event,
    /// <see cref="Completed"/> when the stream ends normally, and
    /// <see cref="Faulted"/> with a mapped <see cref="Mt5GrpcError"/> on failure so
    /// a consumer can resubscribe from the last received transaction time
    /// (User Story 3, FR-014).
    /// </summary>
    public sealed class TradeTransactionSubscription : IDisposable
    {
        private readonly Func<CancellationToken, IAsyncEnumerable<TradeTransactionEvent>> source;
        private readonly CancellationTokenSource cts = new CancellationTokenSource();
        private Task? runTask;
        private int started;
        private bool disposed;

        internal TradeTransactionSubscription(
            Func<CancellationToken, IAsyncEnumerable<TradeTransactionEvent>> source)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
        }

        /// <summary>Raised once per received trade transaction event.</summary>
        public event EventHandler<TradeTransactionEvent>? TransactionReceived;

        /// <summary>Raised once when the stream ends normally or is stopped.</summary>
        public event EventHandler? Completed;

        /// <summary>Raised once when the stream ends due to a fault, carrying the mapped error.</summary>
        public event EventHandler<Mt5GrpcError>? Faulted;

        /// <summary>
        /// Start consuming the stream on a background task. Idempotent-guarded:
        /// throws if already started. Use <see cref="RunAsync"/> instead to await
        /// the stream to completion.
        /// </summary>
        public void Start()
        {
            if (Interlocked.CompareExchange(ref started, 1, 0) != 0)
            {
                throw new InvalidOperationException("The subscription has already been started.");
            }

            runTask = PumpAsync(cts.Token);
        }

        /// <summary>
        /// Run the subscription to completion, awaitable. Links
        /// <paramref name="cancellationToken"/> with the subscription's own token so
        /// either can stop it. Events are raised exactly as with <see cref="Start"/>.
        /// </summary>
        public Task RunAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref started, 1, 0) != 0)
            {
                throw new InvalidOperationException("The subscription has already been started.");
            }

            if (cancellationToken.CanBeCanceled)
            {
                var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);
                runTask = PumpAsync(linked.Token).ContinueWith(
                    t => { linked.Dispose(); return t; },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default).Unwrap();
            }
            else
            {
                runTask = PumpAsync(cts.Token);
            }

            return runTask;
        }

        /// <summary>Request a graceful stop; the stream ends and <see cref="Completed"/> is raised.</summary>
        public void Stop()
        {
            if (!disposed)
            {
                cts.Cancel();
            }
        }

        private async Task PumpAsync(CancellationToken token)
        {
            try
            {
                await foreach (var evt in source(token).WithCancellation(token).ConfigureAwait(false))
                {
                    TransactionReceived?.Invoke(this, evt);
                }

                Completed?.Invoke(this, EventArgs.Empty);
            }
            catch (Mt5GrpcClientException exception) when (exception.Error != null)
            {
                Faulted?.Invoke(this, exception.Error);
            }
            catch (OperationCanceledException)
            {
                // Graceful stop / disconnect — treat as normal completion so the
                // consumer can resubscribe from the last received transaction time.
                Completed?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            cts.Cancel();
            cts.Dispose();
        }
    }
}
