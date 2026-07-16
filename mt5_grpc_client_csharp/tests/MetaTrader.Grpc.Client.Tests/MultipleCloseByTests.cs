using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Metatrader.V1;
using Xunit;

namespace MetaTrader.Grpc.Client.Tests
{
    public sealed class MultipleCloseByTests
    {
        [Fact]
        public async Task Blank_symbol_fails_before_discovery_or_send()
        {
            var harness = new BatchHarness();

            var result = await harness.Executor.ClosePositionsByAsync(
                new ClosePositionsByRequest(" "),
                null,
                CancellationToken.None);

            Assert.Equal(MultipleCloseByStatus.ValidationFailed, result.Status);
            Assert.NotNull(result.BatchError);
            Assert.Empty(harness.PositionCalls);
            Assert.Empty(harness.SendCalls);
        }

        [Fact]
        public async Task Discovery_freezes_scope_pairs_fifo_and_excludes_new_positions()
        {
            var harness = new BatchHarness();
            harness.Positions.Enqueue(SuccessPositions(
                Position(20, 0, 7, 1, 1),
                Position(10, 0, 7, 1, 1),
                Position(40, 1, 7, 1, 1),
                Position(50, 1, 7, 1, 3),
                Position(60, 1, 8, 1, 1),
                Position(70, 0, 7, 1, 1, "GBPUSD")));
            harness.Positions.Enqueue(SuccessPositions(
                Position(20, 0, 7, 1, 1),
                Position(10, 0, 7, 1, 1),
                Position(40, 1, 7, 1, 1),
                Position(50, 1, 7, 1, 3),
                Position(999, 1, 7, 1, 0)));
            harness.Positions.Enqueue(SuccessPositions(
                Position(20, 0, 7, 1, 1),
                Position(50, 1, 7, 1, 3),
                Position(999, 0, 7, 1, 0)));
            harness.Positions.Enqueue(SuccessPositions(Position(999, 1, 7, 1, 0)));
            harness.Sends.Enqueue(SuccessSend(10009));
            harness.Sends.Enqueue(SuccessSend(10009));

            var request = new ClosePositionsByRequest("EURUSD") { Magic = 7, Comment = "batch-audit" };
            var result = await harness.Executor.ClosePositionsByAsync(request, null, CancellationToken.None);

            Assert.Equal(MultipleCloseByStatus.Completed, result.Status);
            Assert.Equal(new long[] { 10, 20, 40, 50 }, result.FrozenTickets);
            Assert.Equal(4, harness.PositionCalls.Count);
            Assert.All(harness.PositionCalls, call => Assert.Equal("EURUSD", call.Request.Symbol));
            Assert.Equal(2, harness.SendCalls.Count);
            Assert.Collection(
                result.Pairs,
                pair => AssertPair(pair, 1, 10, 40, PairAttemptState.Attempted),
                pair => AssertPair(pair, 2, 20, 50, PairAttemptState.Attempted));
            Assert.All(result.Pairs, pair => Assert.Equal(TradeExecutionStatus.Completed, pair.OperationResult!.ExecutionStatus));
            Assert.DoesNotContain(result.Pairs, pair => pair.PositionTicket == 999 || pair.OppositePositionTicket == 999);
            Assert.All(harness.SendCalls, call => Assert.Equal("batch-audit", call.Request.TradeRequest.Comment));
            Assert.Empty(result.Remainders);
            Assert.Equal("EURUSD", request.Symbol);
            Assert.Equal(7, request.Magic);
            Assert.Equal("batch-audit", request.Comment);
        }

        [Fact]
        public async Task Equal_open_times_break_ties_by_ascending_ticket_and_buy_is_primary()
        {
            var harness = new BatchHarness();
            var positions = new[]
            {
                Position(8, 1, 0, 1, 1),
                Position(3, 0, 0, 1, 1),
                Position(7, 1, 0, 1, 1),
                Position(2, 0, 0, 1, 1)
            };
            harness.Positions.Enqueue(SuccessPositions(positions));
            harness.Positions.Enqueue(SuccessPositions(positions));
            harness.Positions.Enqueue(SuccessPositions(Position(3, 0, 0, 1, 1), Position(8, 1, 0, 1, 1)));
            harness.Positions.Enqueue(SuccessPositions());
            harness.Sends.Enqueue(SuccessSend(10009));
            harness.Sends.Enqueue(SuccessSend(10009));

            var result = await harness.Executor.ClosePositionsByAsync(
                new ClosePositionsByRequest("EURUSD"),
                null,
                CancellationToken.None);

            Assert.Collection(
                result.Pairs,
                pair => AssertPair(pair, 1, 2, 7, PairAttemptState.Attempted),
                pair => AssertPair(pair, 2, 3, 8, PairAttemptState.Attempted));
            Assert.Equal(2, harness.SendCalls[0].Request.TradeRequest.Position);
            Assert.Equal(7, harness.SendCalls[0].Request.TradeRequest.PositionBy);
        }

        [Fact]
        public async Task Empty_discovery_completes_without_refresh_or_send()
        {
            var harness = new BatchHarness();
            harness.Positions.Enqueue(SuccessPositions());

            var result = await harness.Executor.ClosePositionsByAsync(
                new ClosePositionsByRequest("EURUSD"),
                null,
                CancellationToken.None);

            Assert.Equal(MultipleCloseByStatus.Completed, result.Status);
            Assert.Empty(result.FrozenTickets);
            Assert.Empty(result.Pairs);
            Assert.Empty(result.Remainders);
            Assert.Single(harness.PositionCalls);
            Assert.Empty(harness.SendCalls);
        }

        [Fact]
        public async Task Discovery_with_one_side_reports_ordered_unmatched_remainders_without_send()
        {
            var harness = new BatchHarness();
            harness.Positions.Enqueue(SuccessPositions(
                Position(12, 0, 0, 0.5, 2),
                Position(11, 0, 0, 1.0, 1)));

            var result = await harness.Executor.ClosePositionsByAsync(
                new ClosePositionsByRequest("EURUSD"),
                null,
                CancellationToken.None);

            Assert.Equal(new long[] { 11, 12 }, result.FrozenTickets);
            Assert.Empty(result.Pairs);
            Assert.Collection(
                result.Remainders,
                remainder => AssertRemainder(remainder, 11, 1.0, PositionRemainderReason.NoOpposite),
                remainder => AssertRemainder(remainder, 12, 0.5, PositionRemainderReason.NoOpposite));
            Assert.Single(harness.PositionCalls);
            Assert.Empty(harness.SendCalls);
        }

        [Fact]
        public async Task Rejected_second_pair_is_retained_and_later_independent_pair_continues()
        {
            var harness = new BatchHarness();
            var all = new[]
            {
                Position(1, 0, 0, 1, 1), Position(2, 0, 0, 1, 2), Position(3, 0, 0, 1, 3),
                Position(4, 1, 0, 1, 1), Position(5, 1, 0, 1, 2), Position(6, 1, 0, 1, 3)
            };
            harness.Positions.Enqueue(SuccessPositions(all));
            harness.Positions.Enqueue(SuccessPositions(all));
            harness.Positions.Enqueue(SuccessPositions(all.Skip(1).Where(p => p.Ticket != 4).ToArray()));
            harness.Positions.Enqueue(SuccessPositions(Position(2, 0, 0, 1, 2), Position(3, 0, 0, 1, 3), Position(5, 1, 0, 1, 2), Position(6, 1, 0, 1, 3)));
            harness.Positions.Enqueue(SuccessPositions());
            harness.Sends.Enqueue(SuccessSend(10009));
            harness.Sends.Enqueue(SuccessSend(10006));
            harness.Sends.Enqueue(SuccessSend(10009));

            var result = await harness.Executor.ClosePositionsByAsync(
                new ClosePositionsByRequest("EURUSD"), null, CancellationToken.None);

            Assert.Equal(MultipleCloseByStatus.Completed, result.Status);
            Assert.Collection(
                result.Pairs,
                pair => AssertPair(pair, 1, 1, 4, PairAttemptState.Attempted),
                pair =>
                {
                    AssertPair(pair, 2, 2, 5, PairAttemptState.Attempted);
                    Assert.Equal(TradeExecutionStatus.RejectedOrFailed, pair.OperationResult!.ExecutionStatus);
                },
                pair => AssertPair(pair, 3, 3, 6, PairAttemptState.Attempted));
            Assert.Equal(3, harness.SendCalls.Count);
            Assert.Contains(result.Remainders, r => r.Ticket == 2 && r.Reason == PositionRemainderReason.WithheldAfterPair);
            Assert.Contains(result.Remainders, r => r.Ticket == 5 && r.Reason == PositionRemainderReason.WithheldAfterPair);
        }

        [Fact]
        public async Task Accepted_unknown_and_transport_uncertain_pairs_are_each_withheld_without_retry()
        {
            var harness = new BatchHarness();
            var all = new[]
            {
                Position(1, 0, 0, 1, 1), Position(2, 0, 0, 1, 2), Position(3, 0, 0, 1, 3),
                Position(4, 1, 0, 1, 1), Position(5, 1, 0, 1, 2), Position(6, 1, 0, 1, 3)
            };
            harness.Positions.Enqueue(SuccessPositions(all));
            harness.Positions.Enqueue(SuccessPositions(all));
            harness.Positions.Enqueue(SuccessPositions(all));
            harness.Positions.Enqueue(SuccessPositions(all));
            harness.Sends.Enqueue(SuccessSend(10008));
            harness.Sends.Enqueue(SuccessSend(19999));
            harness.Sends.Enqueue(Failure<OrderSendResponse>(StatusCode.Unavailable, "uncertain"));

            var result = await harness.Executor.ClosePositionsByAsync(
                new ClosePositionsByRequest("EURUSD"), null, CancellationToken.None);

            Assert.Equal(MultipleCloseByStatus.Completed, result.Status);
            Assert.Equal(
                new[]
                {
                    TradeExecutionStatus.AcceptedOrPlaced,
                    TradeExecutionStatus.Unknown,
                    (TradeExecutionStatus?)null
                },
                result.Pairs.Select(p => p.OperationResult!.ExecutionStatus));
            Assert.Equal(3, harness.SendCalls.Count);
            Assert.Equal(6, result.Remainders.Count(r => r.Reason == PositionRemainderReason.WithheldAfterPair));
        }

        [Fact]
        public async Task Partial_completion_refreshes_and_repairs_the_surviving_position()
        {
            var harness = new BatchHarness();
            harness.Positions.Enqueue(SuccessPositions(
                Position(1, 0, 0, 2, 1), Position(2, 1, 0, 1, 1), Position(3, 1, 0, 1, 2)));
            harness.Positions.Enqueue(SuccessPositions(
                Position(1, 0, 0, 2, 1), Position(2, 1, 0, 1, 1), Position(3, 1, 0, 1, 2)));
            harness.Positions.Enqueue(SuccessPositions(
                Position(1, 0, 0, 1, 1), Position(3, 1, 0, 1, 2)));
            harness.Positions.Enqueue(SuccessPositions());
            harness.Sends.Enqueue(SuccessSend(10010));
            harness.Sends.Enqueue(SuccessSend(10009));

            var result = await harness.Executor.ClosePositionsByAsync(
                new ClosePositionsByRequest("EURUSD"), null, CancellationToken.None);

            Assert.Collection(
                result.Pairs,
                pair => AssertPair(pair, 1, 1, 2, PairAttemptState.Attempted),
                pair => AssertPair(pair, 2, 1, 3, PairAttemptState.Attempted));
            Assert.Equal(2, harness.SendCalls.Count);
            Assert.Empty(result.Remainders);
        }

        [Fact]
        public async Task Missing_and_newly_ineligible_frozen_tickets_are_reported_without_send()
        {
            var harness = new BatchHarness();
            harness.Positions.Enqueue(SuccessPositions(
                Position(1, 0, 0, 1, 1), Position(2, 0, 0, 1, 2),
                Position(3, 1, 0, 1, 3), Position(4, 1, 0, 1, 4)));
            harness.Positions.Enqueue(SuccessPositions(
                Position(2, 0, 0, 0, 2),
                Position(3, 1, 0, 1, 3),
                Position(4, 2, 0, 1, 4)));

            var result = await harness.Executor.ClosePositionsByAsync(
                new ClosePositionsByRequest("EURUSD"), null, CancellationToken.None);

            Assert.Empty(result.Pairs);
            Assert.Empty(harness.SendCalls);
            Assert.Contains(result.Remainders, r => r.Ticket == 1 && r.Reason == PositionRemainderReason.MissingFromRefresh);
            Assert.Contains(result.Remainders, r => r.Ticket == 2 && r.Reason == PositionRemainderReason.BecameIneligible);
            Assert.Contains(result.Remainders, r => r.Ticket == 3 && r.Reason == PositionRemainderReason.NoOpposite);
            Assert.Contains(result.Remainders, r => r.Ticket == 4 && r.Reason == PositionRemainderReason.BecameIneligible);
        }

        [Fact]
        public async Task Refresh_failure_retains_prior_outcome_and_stops_later_sends()
        {
            var harness = new BatchHarness();
            var all = new[]
            {
                Position(1, 0, 0, 1, 1), Position(2, 0, 0, 1, 2),
                Position(3, 1, 0, 1, 1), Position(4, 1, 0, 1, 2)
            };
            harness.Positions.Enqueue(SuccessPositions(all));
            harness.Positions.Enqueue(SuccessPositions(all));
            harness.Positions.Enqueue(Failure<PositionsGetResponse>(StatusCode.Unavailable, "refresh down"));
            harness.Sends.Enqueue(SuccessSend(10009));

            var result = await harness.Executor.ClosePositionsByAsync(
                new ClosePositionsByRequest("EURUSD"), null, CancellationToken.None);

            Assert.Equal(MultipleCloseByStatus.RefreshFailed, result.Status);
            Assert.NotNull(result.BatchError);
            Assert.Single(result.Pairs);
            Assert.Single(harness.SendCalls);
            Assert.Contains(result.Remainders, r => r.Ticket == 2 && r.Reason == PositionRemainderReason.UnattemptedAfterStop);
            Assert.Contains(result.Remainders, r => r.Ticket == 4 && r.Reason == PositionRemainderReason.UnattemptedAfterStop);
        }

        [Fact]
        public async Task Default_deadline_is_captured_once_and_shared_with_one_token()
        {
            var now = new DateTime(2032, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var harness = new BatchHarness(TimeSpan.FromSeconds(30), () => now);
            var all = new[] { Position(1, 0, 0, 1, 1), Position(2, 1, 0, 1, 1) };
            harness.Positions.Enqueue(SuccessPositions(all));
            harness.Positions.Enqueue(SuccessPositions(all));
            harness.Sends.Enqueue(SuccessSend(10006));
            using var cancellation = new CancellationTokenSource();

            await harness.Executor.ClosePositionsByAsync(
                new ClosePositionsByRequest("EURUSD"), null, cancellation.Token);

            var expected = now.AddSeconds(30);
            Assert.All(harness.PositionCalls, call =>
            {
                Assert.Equal(expected, call.Deadline);
                Assert.Equal(cancellation.Token, call.CancellationToken);
            });
            Assert.All(harness.SendCalls, call =>
            {
                Assert.Equal(expected, call.Deadline);
                Assert.Equal(cancellation.Token, call.CancellationToken);
            });
        }

        [Fact]
        public async Task Explicit_deadline_is_forwarded_unchanged_to_every_inner_call()
        {
            var harness = new BatchHarness();
            var all = new[] { Position(1, 0, 0, 1, 1), Position(2, 1, 0, 1, 1) };
            harness.Positions.Enqueue(SuccessPositions(all));
            harness.Positions.Enqueue(SuccessPositions(all));
            harness.Sends.Enqueue(SuccessSend(10006));
            var deadline = DateTime.UtcNow.AddMinutes(2);

            await harness.Executor.ClosePositionsByAsync(
                new ClosePositionsByRequest("EURUSD"), deadline, CancellationToken.None);

            Assert.All(harness.PositionCalls, call => Assert.Equal(deadline, call.Deadline));
            Assert.All(harness.SendCalls, call => Assert.Equal(deadline, call.Deadline));
        }

        [Fact]
        public async Task Pre_cancelled_batch_makes_zero_calls()
        {
            var harness = new BatchHarness();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            var result = await harness.Executor.ClosePositionsByAsync(
                new ClosePositionsByRequest("EURUSD"), null, cancellation.Token);

            Assert.Equal(MultipleCloseByStatus.Cancelled, result.Status);
            Assert.Empty(harness.PositionCalls);
            Assert.Empty(harness.SendCalls);
        }

        [Fact]
        public async Task Expired_explicit_deadline_makes_zero_calls()
        {
            var now = new DateTime(2032, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var harness = new BatchHarness(utcNow: () => now);

            var result = await harness.Executor.ClosePositionsByAsync(
                new ClosePositionsByRequest("EURUSD"), now.AddTicks(-1), CancellationToken.None);

            Assert.Equal(MultipleCloseByStatus.DeadlineExceeded, result.Status);
            Assert.Empty(harness.PositionCalls);
            Assert.Empty(harness.SendCalls);
        }

        [Fact]
        public async Task Cancellation_during_send_retains_attempt_and_materializes_later_pair()
        {
            var harness = new BatchHarness();
            var all = new[]
            {
                Position(1, 0, 0, 1, 1), Position(2, 0, 0, 1, 2),
                Position(3, 1, 0, 1, 1), Position(4, 1, 0, 1, 2)
            };
            harness.Positions.Enqueue(SuccessPositions(all));
            harness.Positions.Enqueue(SuccessPositions(all));
            harness.Sends.Enqueue(Failure<OrderSendResponse>(StatusCode.Cancelled, "cancelled"));

            var result = await harness.Executor.ClosePositionsByAsync(
                new ClosePositionsByRequest("EURUSD"), null, CancellationToken.None);

            Assert.Equal(MultipleCloseByStatus.Cancelled, result.Status);
            Assert.Equal(2, result.Pairs.Count);
            AssertPair(result.Pairs[0], 1, 1, 3, PairAttemptState.Attempted);
            AssertPair(result.Pairs[1], 2, 2, 4, PairAttemptState.Unattempted);
            Assert.Single(harness.SendCalls);
        }

        [Fact]
        public async Task Deadline_during_refresh_stops_before_send_and_materializes_known_pairs()
        {
            var harness = new BatchHarness();
            var all = new[]
            {
                Position(1, 0, 0, 1, 1), Position(2, 0, 0, 1, 2),
                Position(3, 1, 0, 1, 1), Position(4, 1, 0, 1, 2)
            };
            harness.Positions.Enqueue(SuccessPositions(all));
            harness.Positions.Enqueue(Failure<PositionsGetResponse>(StatusCode.DeadlineExceeded, "deadline"));

            var result = await harness.Executor.ClosePositionsByAsync(
                new ClosePositionsByRequest("EURUSD"), DateTime.UtcNow.AddMinutes(1), CancellationToken.None);

            Assert.Equal(MultipleCloseByStatus.DeadlineExceeded, result.Status);
            Assert.Equal(2, result.Pairs.Count);
            Assert.All(result.Pairs, pair => Assert.Equal(PairAttemptState.Unattempted, pair.AttemptState));
            Assert.Empty(harness.SendCalls);
        }

        [Fact]
        public async Task Discovery_failure_returns_batch_error_without_send()
        {
            var harness = new BatchHarness();
            harness.Positions.Enqueue(Failure<PositionsGetResponse>(StatusCode.Unavailable, "discovery down"));

            var result = await harness.Executor.ClosePositionsByAsync(
                new ClosePositionsByRequest("EURUSD"), null, CancellationToken.None);

            Assert.Equal(MultipleCloseByStatus.DiscoveryFailed, result.Status);
            Assert.NotNull(result.BatchError);
            Assert.Single(harness.PositionCalls);
            Assert.Empty(harness.SendCalls);
            Assert.Empty(result.FrozenTickets);
        }

        private static void AssertPair(
            CloseByPairOutcome outcome,
            int index,
            long buyTicket,
            long sellTicket,
            PairAttemptState state)
        {
            Assert.Equal(index, outcome.PairIndex);
            Assert.Equal(buyTicket, outcome.PositionTicket);
            Assert.Equal(sellTicket, outcome.OppositePositionTicket);
            Assert.Equal(state, outcome.AttemptState);
        }

        private static void AssertRemainder(
            PositionRemainder remainder,
            long ticket,
            double? volume,
            PositionRemainderReason reason)
        {
            Assert.Equal(ticket, remainder.Ticket);
            Assert.Equal(volume, remainder.LastKnownVolume);
            Assert.Equal(reason, remainder.Reason);
        }

        private static Position Position(
            long ticket,
            int type,
            int magic,
            double volume,
            long seconds,
            string symbol = "EURUSD")
        {
            return new Position
            {
                Ticket = ticket,
                Type = type,
                Magic = magic,
                Volume = volume,
                Symbol = symbol,
                Time = new Timestamp { Seconds = seconds }
            };
        }

        private static Mt5GrpcResult<PositionsGetResponse> SuccessPositions(params Position[] positions)
        {
            var response = new PositionsGetResponse();
            response.Positions.Add(positions);
            return Mt5GrpcResult<PositionsGetResponse>.Success(response);
        }

        private static Mt5GrpcResult<OrderSendResponse> SuccessSend(int retcode)
        {
            return Mt5GrpcResult<OrderSendResponse>.Success(new OrderSendResponse
            {
                TradeResult = new TradeResult { Retcode = retcode }
            });
        }

        private static Mt5GrpcResult<T> Failure<T>(StatusCode statusCode, string message)
        {
            return Mt5GrpcResult<T>.Failure(new Mt5GrpcError
            {
                Operation = "scripted",
                StatusCode = statusCode,
                Message = message
            });
        }

        private sealed class BatchHarness
        {
            public BatchHarness(TimeSpan? defaultDeadline = null, Func<DateTime>? utcNow = null)
            {
                Executor = new TradeLifecycleExecutor(SendAsync, GetPositionsAsync, defaultDeadline, utcNow: utcNow);
            }

            public TradeLifecycleExecutor Executor { get; }
            public Queue<Mt5GrpcResult<PositionsGetResponse>> Positions { get; } = new Queue<Mt5GrpcResult<PositionsGetResponse>>();
            public Queue<Mt5GrpcResult<OrderSendResponse>> Sends { get; } = new Queue<Mt5GrpcResult<OrderSendResponse>>();
            public List<PositionCall> PositionCalls { get; } = new List<PositionCall>();
            public List<SendCall> SendCalls { get; } = new List<SendCall>();

            private Task<Mt5GrpcResult<OrderSendResponse>> SendAsync(
                OrderSendRequest request,
                DateTime? deadline,
                CancellationToken cancellationToken)
            {
                SendCalls.Add(new SendCall(request, deadline, cancellationToken));
                return Task.FromResult(Sends.Dequeue());
            }

            private Task<Mt5GrpcResult<PositionsGetResponse>> GetPositionsAsync(
                PositionsGetRequest request,
                DateTime? deadline,
                CancellationToken cancellationToken)
            {
                PositionCalls.Add(new PositionCall(request, deadline, cancellationToken));
                return Task.FromResult(Positions.Dequeue());
            }
        }

        private sealed class PositionCall
        {
            public PositionCall(PositionsGetRequest request, DateTime? deadline, CancellationToken cancellationToken)
            {
                Request = request;
                Deadline = deadline;
                CancellationToken = cancellationToken;
            }

            public PositionsGetRequest Request { get; }
            public DateTime? Deadline { get; }
            public CancellationToken CancellationToken { get; }
        }

        private sealed class SendCall
        {
            public SendCall(OrderSendRequest request, DateTime? deadline, CancellationToken cancellationToken)
            {
                Request = request;
                Deadline = deadline;
                CancellationToken = cancellationToken;
            }

            public OrderSendRequest Request { get; }
            public DateTime? Deadline { get; }
            public CancellationToken CancellationToken { get; }
        }
    }
}
