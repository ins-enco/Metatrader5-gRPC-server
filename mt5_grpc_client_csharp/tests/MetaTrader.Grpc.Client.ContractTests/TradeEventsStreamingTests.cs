using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Metatrader.V1;
using Xunit;

namespace MetaTrader.Grpc.Client.ContractTests
{
    public sealed class TradeEventsStreamingTests
    {
        // --- Generated surface (US1): the first streaming RPC exists on the client ---

        [Fact]
        public void Generated_contract_exposes_trade_events_streaming_service()
        {
            var assembly = typeof(AccountInfoService).Assembly;

            foreach (var pair in ProtoContractCatalog.StreamingServices)
            {
                var serviceType = assembly.GetType("Metatrader.V1." + pair.Key);
                Assert.NotNull(serviceType);
                var clientType = serviceType!.GetNestedType(pair.Key + "Client", BindingFlags.Public);
                Assert.NotNull(clientType);
                foreach (var rpc in pair.Value)
                {
                    Assert.Contains(clientType!.GetMethods(), method => method.Name == rpc);
                }
            }
        }

        [Fact]
        public void Client_exposes_async_enumerable_and_event_surfaces()
        {
            // Compile-time proof of both FR-011 surfaces plus a runtime signature check.
            var asyncMethod = typeof(Mt5GrpcClient).GetMethod(nameof(Mt5GrpcClient.SubscribeTradeTransactionsAsync));
            Assert.NotNull(asyncMethod);
            Assert.Equal(typeof(IAsyncEnumerable<TradeTransactionEvent>), asyncMethod!.ReturnType);

            var eventMethod = typeof(Mt5GrpcClient).GetMethod(nameof(Mt5GrpcClient.SubscribeTradeTransactions));
            Assert.NotNull(eventMethod);
            Assert.Equal(typeof(TradeTransactionSubscription), eventMethod!.ReturnType);
        }

        [Fact]
        public void Event_message_is_typed_with_required_fields()
        {
            var evt = new TradeTransactionEvent
            {
                DealTicket = 42,
                OrderTicket = 7,
                PositionTicket = 9,
                Symbol = "EURUSD",
                Volume = 1.5,
                Price = 1.2345,
                Profit = 10.0,
                TimeMsc = 1_700_000_000_000,
                Type = 1,
                Entry = 2,
            };

            Assert.Equal(42UL, evt.DealTicket);
            Assert.Equal("EURUSD", evt.Symbol);
            Assert.Equal(1, evt.Type);
            Assert.Null(evt.Error);
        }

        // --- await foreach consumption (US1) over a controllable async sequence ---

        [Fact]
        public async Task Await_foreach_yields_each_event_once_in_order()
        {
            var source = MakeEvents(1, 2, 3);
            var received = new List<ulong>();

            await foreach (var evt in source)
            {
                received.Add(evt.DealTicket);
            }

            Assert.Equal(new ulong[] { 1, 2, 3 }, received);
        }

        // --- Event wrapper (US1 convenience + US3 completion/error signalling) ---

        [Fact]
        public async Task Subscription_raises_event_per_transaction_then_completes()
        {
            var subscription = new TradeTransactionSubscription(_ => MakeEvents(1, 2, 3));
            var received = new List<ulong>();
            var completed = false;
            Mt5GrpcError? faulted = null;

            subscription.TransactionReceived += (_, evt) => received.Add(evt.DealTicket);
            subscription.Completed += (_, _) => completed = true;
            subscription.Faulted += (_, error) => faulted = error;

            await subscription.RunAsync();

            Assert.Equal(new ulong[] { 1, 2, 3 }, received);
            Assert.True(completed);
            Assert.Null(faulted);
        }

        [Fact]
        public async Task Subscription_raises_faulted_with_mapped_error_on_terminal_failure()
        {
            var error = new Mt5GrpcError { Operation = "TradeEventsService.SubscribeTradeTransactions", Mt5ErrorCode = -10005, Message = "no history" };
            var subscription = new TradeTransactionSubscription(_ => ThrowsAfter(new Mt5GrpcClientException(error)));
            var completed = false;
            Mt5GrpcError? faulted = null;

            subscription.Completed += (_, _) => completed = true;
            subscription.Faulted += (_, e) => faulted = e;

            await subscription.RunAsync();

            Assert.NotNull(faulted);
            Assert.Equal(-10005, faulted!.Mt5ErrorCode);
            Assert.False(completed);
        }

        [Fact]
        public async Task Subscription_stops_on_cancellation()
        {
            var subscription = new TradeTransactionSubscription(token => InfiniteEvents(token));
            var received = 0;
            subscription.TransactionReceived += (_, _) => received++;

            using var cts = new CancellationTokenSource();
            var run = subscription.RunAsync(cts.Token);

            // Let a few events flow, then cancel; the run must complete promptly.
            await Task.Delay(50);
            cts.Cancel();

            await run.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(run.IsCompleted);
        }

        // --- helpers: controllable async sequences (no live server needed) ---

        private static async IAsyncEnumerable<TradeTransactionEvent> MakeEvents(params ulong[] tickets)
        {
            foreach (var ticket in tickets)
            {
                await Task.Yield();
                yield return new TradeTransactionEvent { DealTicket = ticket, TimeMsc = (long)ticket };
            }
        }

        private static async IAsyncEnumerable<TradeTransactionEvent> ThrowsAfter(Exception exception)
        {
            await Task.Yield();
            throw exception;
#pragma warning disable CS0162 // unreachable — required to make this an iterator
            yield break;
#pragma warning restore CS0162
        }

        private static async IAsyncEnumerable<TradeTransactionEvent> InfiniteEvents(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ulong ticket = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
                yield return new TradeTransactionEvent { DealTicket = ++ticket, TimeMsc = (long)ticket };
            }
        }
    }
}
