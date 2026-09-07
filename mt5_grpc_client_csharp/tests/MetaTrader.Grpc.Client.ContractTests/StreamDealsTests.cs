using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using MetaTrader.Grpc.Client.ContractTests.Fixtures;
using Metatrader.V1;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MetaTrader.Grpc.Client.ContractTests
{
    /// <summary>
    /// The deal-history streaming surface (specs/007-csharp-stream-deals,
    /// contracts/client-surface.md sections B, C, D, F). Every case runs against
    /// <see cref="FakeStreamingChannel"/> - no socket, no live broker, no MT5
    /// terminal - so the real public methods, the real generated stub and the real
    /// <c>Mt5GrpcStreamingInvoker</c> are all under test.
    /// </summary>
    public sealed class StreamDealsTests
    {
        private const string StreamOperation = "TradeHistoryService.StreamDeals";

        // --- T005: generated surface (contract F1, F2; FR-012) ------------------

        [Fact]
        public void Generated_trade_history_client_exposes_stream_deals()
        {
            var client = typeof(TradeHistoryService.TradeHistoryServiceClient);

            var streamDeals = client.GetMethods().Where(method => method.Name == "StreamDeals").ToList();

            Assert.NotEmpty(streamDeals);
            Assert.Contains(
                streamDeals,
                method => method.ReturnType == typeof(AsyncServerStreamingCall<DealsResponse>));
        }

        [Fact]
        public void Deals_request_exposes_chunk_size_with_proto3_presence_on_field_five()
        {
            Assert.NotNull(typeof(DealsRequest).GetProperty("ChunkSize"));
            Assert.NotNull(typeof(DealsRequest).GetProperty("HasChunkSize"));
            Assert.NotNull(typeof(DealsRequest).GetMethod("ClearChunkSize"));
            Assert.Equal(5, DealsRequest.ChunkSizeFieldNumber);

            // Presence round-trips: unset stays unset, and a cleared field is unset
            // again, which is what keeps the server the owner of the 500 default.
            var request = new DealsRequest();
            Assert.False(request.HasChunkSize);
            request.ChunkSize = 250;
            Assert.True(request.HasChunkSize);
            Assert.Equal(250u, request.ChunkSize);
            request.ClearChunkSize();
            Assert.False(request.HasChunkSize);
        }

        // --- T006: public signature (contract section B; FR-001) ---------------

        [Fact]
        public void Stream_deals_async_has_the_contracted_signature()
        {
            var method = typeof(Mt5GrpcClient).GetMethod(nameof(Mt5GrpcClient.StreamDealsAsync));

            Assert.NotNull(method);
            Assert.Equal(typeof(IAsyncEnumerable<DealsResponse>), method!.ReturnType);
            Assert.Equal(
                new[] { typeof(DealsRequest), typeof(DateTime?), typeof(CancellationToken) },
                method.GetParameters().Select(parameter => parameter.ParameterType));
            Assert.All(method.GetParameters(), parameter => Assert.True(parameter.IsOptional));
        }

        // --- T007: delivery (US1-AC1, US1-AC2; FR-001, FR-003; contract B1) ----

        [Fact]
        public async Task Large_history_streams_as_bounded_chunks_in_server_order()
        {
            var channel = new FakeStreamingChannel();
            channel.History.AddRange(MakeHistory(1571));
            using var client = ClientOver(channel);

            var chunks = new List<DealsResponse>();
            await foreach (var chunk in client.StreamDealsAsync(WholeHistory()))
            {
                chunks.Add(chunk);
            }

            // The server's default chunk size is 500, so 1571 deals arrive as
            // 500 + 500 + 500 + 71.
            Assert.Equal(4, chunks.Count);
            Assert.Equal(new[] { 500, 500, 500, 71 }, chunks.Select(chunk => chunk.Deals.Count));

            var streamed = chunks.SelectMany(chunk => chunk.Deals).ToList();
            Assert.Equal(1571, streamed.Count);
            Assert.Equal(
                channel.History.Select(deal => deal.Ticket),
                streamed.Select(deal => deal.Ticket));
        }

        [Fact]
        public async Task Empty_filtered_history_yields_no_chunk_and_completes_normally()
        {
            var channel = new FakeStreamingChannel();
            channel.History.AddRange(MakeHistory(10));
            using var client = ClientOver(channel);

            var chunks = 0;
            // A window that predates every deal: a legitimately empty read.
            var request = new DealsRequest { TimeFilter = new TimeFilter { DateFrom = 1, DateTo = 2 } };
            await foreach (var _ in client.StreamDealsAsync(request))
            {
                chunks++;
            }

            Assert.Equal(0, chunks);
            Assert.Equal(1, channel.InvocationCount("StreamDeals"));
        }

        // --- T008: failures (US1-AC3; FR-002, FR-006; contract B3, B4) ---------

        [Fact]
        public async Task In_band_error_message_terminates_enumeration_with_the_mapped_error()
        {
            var channel = new FakeStreamingChannel
            {
                StreamScript = new FakeStreamScript().YieldError(-10005, "No history found"),
            };
            using var client = ClientOver(channel);

            var chunks = 0;
            var exception = await Assert.ThrowsAsync<Mt5GrpcClientException>(async () =>
            {
                await foreach (var _ in client.StreamDealsAsync(WholeHistory()))
                {
                    chunks++;
                }
            });

            Assert.Equal(0, chunks);
            Assert.NotNull(exception.Error);
            Assert.Equal(-10005, exception.Error!.Mt5ErrorCode);
            Assert.Equal("No history found", exception.Error.Message);
            Assert.Equal("No history found", exception.Error.Mt5ErrorMessage);
            Assert.Equal(StreamOperation, exception.Error.Operation);
        }

        [Fact]
        public async Task Unimplemented_from_a_pre_0_4_0_server_fails_without_falling_back_to_get_deals()
        {
            var channel = new FakeStreamingChannel
            {
                StreamScript = new FakeStreamScript()
                    .Fault(StatusCode.Unimplemented, "Method not found: StreamDeals"),
            };
            channel.History.AddRange(MakeHistory(1571));
            using var client = ClientOver(channel);

            var exception = await Assert.ThrowsAsync<Mt5GrpcClientException>(async () =>
            {
                await foreach (var _ in client.StreamDealsAsync(WholeHistory()))
                {
                }
            });

            Assert.NotNull(exception.Error);
            Assert.Equal(StatusCode.Unimplemented, exception.Error!.StatusCode);
            Assert.Equal(StreamOperation, exception.Error.Operation);

            // The whole point of FR-006: falling back here would send exactly the
            // oversized single response this feature exists to avoid.
            Assert.Equal(1, channel.InvocationCount("StreamDeals"));
            Assert.Equal(0, channel.InvocationCount("GetDeals"));
        }

        // --- T009: lifetime (US1-AC4, US1-AC5; FR-008, SC-004; contract B5, B6) -

        [Fact]
        public async Task Cancelling_mid_stream_ends_enumeration_promptly_and_releases_the_call()
        {
            var channel = new FakeStreamingChannel
            {
                MessageDelay = TimeSpan.FromMilliseconds(40),
                StreamScript = ScriptOfChunks(200, dealsPerChunk: 5),
            };
            using var client = ClientOver(channel);
            using var cts = new CancellationTokenSource();

            var stopwatch = new Stopwatch();

            // Cancellation surfaces as the mapped cancellation error on
            // Mt5GrpcClientException, not as a raw OperationCanceledException: the
            // invoker converts it so every streaming failure has one shape.
            var exception = await Assert.ThrowsAsync<Mt5GrpcClientException>(async () =>
            {
                await foreach (var _ in client.StreamDealsAsync(WholeHistory(), null, cts.Token))
                {
                    cts.Cancel();
                    stopwatch.Start();
                }
            });

            stopwatch.Stop();
            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(1),
                $"cancellation took {stopwatch.Elapsed} (SC-004 allows 1 s)");
            Assert.True(channel.WasDisposed, "the streaming call was not released on cancellation");

            Assert.NotNull(exception.Error);
            Assert.Equal(StatusCode.Cancelled, exception.Error!.StatusCode);
            Assert.Equal(StreamOperation, exception.Error.Operation);
        }

        [Fact]
        public async Task Elapsed_deadline_ends_enumeration_with_the_mapped_deadline_error()
        {
            var channel = new FakeStreamingChannel
            {
                MessageDelay = TimeSpan.FromMilliseconds(200),
                StreamScript = ScriptOfChunks(20, dealsPerChunk: 5),
            };
            using var client = ClientOver(channel);

            var exception = await Assert.ThrowsAsync<Mt5GrpcClientException>(async () =>
            {
                var deadline = DateTime.UtcNow.AddMilliseconds(300);
                await foreach (var _ in client.StreamDealsAsync(WholeHistory(), deadline))
                {
                }
            });

            Assert.NotNull(exception.Error);
            Assert.Equal(StatusCode.DeadlineExceeded, exception.Error!.StatusCode);
            Assert.Equal(StreamOperation, exception.Error.Operation);
            Assert.True(channel.WasDisposed, "the streaming call was not released on deadline");
        }

        [Fact]
        public async Task Abandoning_enumeration_after_the_first_chunk_releases_the_call()
        {
            var channel = new FakeStreamingChannel();
            channel.History.AddRange(MakeHistory(2000));
            using var client = ClientOver(channel);

            var seen = 0;
            await foreach (var chunk in client.StreamDealsAsync(WholeHistory()))
            {
                seen += chunk.Deals.Count;
                break;
            }

            Assert.Equal(500, seen);
            Assert.True(channel.WasDisposed, "breaking out of await foreach did not release the call");
            Assert.Equal(1, channel.Calls.Single().MessagesRead);
        }

        // --- T010: chunk-size transmission (FR-005, FR-013; contract B2) -------

        [Fact]
        public async Task Unset_chunk_size_is_transmitted_as_unset()
        {
            var channel = new FakeStreamingChannel();
            channel.History.AddRange(MakeHistory(600));
            using var client = ClientOver(channel);

            await DrainAsync(client.StreamDealsAsync(WholeHistory()));

            var transmitted = channel.CapturedRequest("StreamDeals");
            Assert.False(transmitted.HasChunkSize);
            Assert.Equal(0u, transmitted.ChunkSize);
        }

        [Fact]
        public async Task Caller_chunk_size_is_transmitted_verbatim()
        {
            var channel = new FakeStreamingChannel();
            channel.History.AddRange(MakeHistory(600));
            using var client = ClientOver(channel);

            var request = WholeHistory();
            request.ChunkSize = 250;

            var chunks = await DrainAsync(client.StreamDealsAsync(request));

            var transmitted = channel.CapturedRequest("StreamDeals");
            Assert.True(transmitted.HasChunkSize);
            Assert.Equal(250u, transmitted.ChunkSize);
            Assert.Same(request, transmitted);
            Assert.Equal(new[] { 250, 250, 100 }, chunks.Select(chunk => chunk.Deals.Count));
        }

        [Fact]
        public async Task Above_cap_chunk_size_is_transmitted_verbatim_and_clamped_only_by_the_server()
        {
            var channel = new FakeStreamingChannel();
            channel.History.AddRange(MakeHistory(2500));
            using var client = ClientOver(channel);

            var request = WholeHistory();
            request.ChunkSize = 5000;

            var chunks = await DrainAsync(client.StreamDealsAsync(request));

            // Transmitted as asked - the client neither clamps nor warns nor rewrites.
            var transmitted = channel.CapturedRequest("StreamDeals");
            Assert.Equal(5000u, transmitted.ChunkSize);
            Assert.Equal(5000u, request.ChunkSize);

            // The server applied its documented cap of 1000...
            Assert.All(chunks, chunk => Assert.True(chunk.Deals.Count <= FakeStreamingChannel.MaxChunkSize));
            Assert.Equal(new[] { 1000, 1000, 500 }, chunks.Select(chunk => chunk.Deals.Count));

            // ...and every deal still reached the caller, unaltered and in order.
            Assert.Equal(
                channel.History.Select(deal => deal.Ticket),
                chunks.SelectMany(chunk => chunk.Deals).Select(deal => deal.Ticket));
        }

        // --- T011: concurrent streams (edge case; contract B8) -----------------

        [Fact]
        public async Task One_stream_faulting_leaves_a_concurrent_stream_yielding_in_order()
        {
            var channel = new FakeStreamingChannel
            {
                // Keyed on the request so the assignment is deterministic however
                // the two enumerations interleave.
                StreamScriptSelector = request => request.Ticket == 1
                    ? new FakeStreamScript()
                        .YieldDeals(MakeHistory(2, firstTicket: 10))
                        .Fault(StatusCode.Unavailable, "connection reset")
                    : new FakeStreamScript()
                        .YieldDeals(MakeHistory(2, firstTicket: 100))
                        .YieldDeals(MakeHistory(2, firstTicket: 200))
                        .YieldDeals(MakeHistory(2, firstTicket: 300)),
            };
            using var client = ClientOver(channel);

            await using var doomed = client.StreamDealsAsync(ByTicket(1)).GetAsyncEnumerator();
            await using var healthy = client.StreamDealsAsync(ByTicket(2)).GetAsyncEnumerator();

            Assert.True(await healthy.MoveNextAsync());
            Assert.Equal(new ulong[] { 100, 101 }, healthy.Current.Deals.Select(deal => deal.Ticket));

            Assert.True(await doomed.MoveNextAsync());
            await Assert.ThrowsAsync<Mt5GrpcClientException>(async () => await doomed.MoveNextAsync());

            // The surviving stream is untouched: remaining chunks, still in order.
            Assert.True(await healthy.MoveNextAsync());
            Assert.Equal(new ulong[] { 200, 201 }, healthy.Current.Deals.Select(deal => deal.Ticket));
            Assert.True(await healthy.MoveNextAsync());
            Assert.Equal(new ulong[] { 300, 301 }, healthy.Current.Deals.Select(deal => deal.Ticket));
            Assert.False(await healthy.MoveNextAsync());
        }

        [Fact]
        public async Task Cancelling_one_stream_leaves_a_concurrent_stream_yielding_in_order()
        {
            var channel = new FakeStreamingChannel
            {
                StreamScriptSelector = _ => new FakeStreamScript()
                    .YieldDeals(MakeHistory(2, firstTicket: 100))
                    .YieldDeals(MakeHistory(2, firstTicket: 200))
                    .YieldDeals(MakeHistory(2, firstTicket: 300)),
            };
            using var client = ClientOver(channel);
            using var cancelled = new CancellationTokenSource();

            await using var doomed = client
                .StreamDealsAsync(ByTicket(1), null, cancelled.Token).GetAsyncEnumerator();
            await using var healthy = client.StreamDealsAsync(ByTicket(2)).GetAsyncEnumerator();

            Assert.True(await doomed.MoveNextAsync());
            Assert.True(await healthy.MoveNextAsync());

            cancelled.Cancel();
            await Assert.ThrowsAsync<Mt5GrpcClientException>(async () => await doomed.MoveNextAsync());

            Assert.True(await healthy.MoveNextAsync());
            Assert.Equal(new ulong[] { 200, 201 }, healthy.Current.Deals.Select(deal => deal.Ticket));
            Assert.True(await healthy.MoveNextAsync());
            Assert.Equal(new ulong[] { 300, 301 }, healthy.Current.Deals.Select(deal => deal.Ticket));
            Assert.False(await healthy.MoveNextAsync());
        }

        // --- T012: unary / streaming parity (US1-AC6; FR-007, SC-002; D) -------

        [Theory]
        [InlineData("time", false)]
        [InlineData("time", true)]
        [InlineData("ticket", false)]
        [InlineData("ticket", true)]
        [InlineData("position", false)]
        [InlineData("position", true)]
        public async Task Streamed_concatenation_equals_get_deals_for_every_filter_form(
            string filterForm, bool withGroup)
        {
            var channel = new FakeStreamingChannel();
            channel.History.AddRange(MakeParityHistory(1200));
            using var client = ClientOver(channel);

            var streamed = await DrainAsync(
                client.StreamDealsAsync(ParityRequest(filterForm, withGroup)));
            var unary = await client.GetDealsAsync(ParityRequest(filterForm, withGroup));

            Assert.True(unary.IsSuccess, unary.Error?.Message);
            var streamedDeals = streamed.SelectMany(chunk => chunk.Deals).ToList();

            // A vacuous pass would prove nothing, so require the filter to select
            // something first.
            Assert.NotEmpty(streamedDeals);
            Assert.Equal(unary.Value!.Deals.Count, streamedDeals.Count);
            Assert.Equal(
                unary.Value.Deals.Select(deal => deal.Ticket),
                streamedDeals.Select(deal => deal.Ticket));
            Assert.Equal(unary.Value.Deals, streamedDeals);
        }

        [Fact]
        public async Task Group_actually_narrows_the_parity_matrix_so_the_comparison_is_meaningful()
        {
            var channel = new FakeStreamingChannel();
            channel.History.AddRange(MakeParityHistory(1200));
            using var client = ClientOver(channel);

            var all = await DrainAsync(client.StreamDealsAsync(ParityRequest("time", withGroup: false)));
            var grouped = await DrainAsync(client.StreamDealsAsync(ParityRequest("time", withGroup: true)));

            Assert.True(
                grouped.Sum(chunk => chunk.Deals.Count) < all.Sum(chunk => chunk.Deals.Count),
                "the group filter must narrow the result, or the parity matrix is vacuous");
        }

        // --- T013: bounded, credential-free logging (FR-010; contract B9) ------

        [Fact]
        public async Task Successful_read_logs_nothing_per_chunk_or_per_deal()
        {
            var small = await LoggedReadAsync(deals: 100);
            var large = await LoggedReadAsync(deals: 5000);

            // Output must not grow with history size: 20 chunks logs exactly what
            // 1 chunk logs.
            Assert.Empty(small);
            Assert.Empty(large);
            Assert.Equal(small.Count, large.Count);
        }

        [Fact]
        public async Task Failing_read_logs_the_existing_entries_once_and_leaks_no_payload()
        {
            var capture = new CapturingLoggerFactory();
            var channel = new FakeStreamingChannel
            {
                StreamScript = new FakeStreamScript().YieldError(-10005, "No history found"),
            };
            using var client = ClientOver(channel, new Mt5GrpcClientOptions { LoggerFactory = capture });

            var request = WholeHistory();
            request.ChunkSize = 250;
            request.Group = "SECRETGROUP";

            await Assert.ThrowsAsync<Mt5GrpcClientException>(async () =>
                await DrainAsync(client.StreamDealsAsync(request)));

            // Exactly the one Mt5ErrorPayload entry the unary path already emits.
            Assert.Single(capture.Entries);
            Assert.Contains(StreamOperation, capture.Entries[0]);

            // No request payload, no credentials, no account identifiers.
            AssertCarriesNoPayload(capture.Entries);
        }

        [Fact]
        public async Task Transport_failure_logs_bounded_credential_free_entries()
        {
            var capture = new CapturingLoggerFactory();
            var channel = new FakeStreamingChannel
            {
                StreamScript = new FakeStreamScript()
                    .YieldDeals(MakeHistory(3))
                    .Fault(StatusCode.Unavailable, "connection reset"),
            };
            using var client = ClientOver(channel, new Mt5GrpcClientOptions { LoggerFactory = capture });

            var request = WholeHistory();
            request.ChunkSize = 250;
            request.Group = "SECRETGROUP";

            await Assert.ThrowsAsync<Mt5GrpcClientException>(async () =>
                await DrainAsync(client.StreamDealsAsync(request)));

            // One CallFailure for the fault, and nothing for the chunk that succeeded.
            Assert.Single(capture.Entries);
            AssertCarriesNoPayload(capture.Entries);
        }

        private static void AssertCarriesNoPayload(IReadOnlyList<string> entries)
        {
            foreach (var entry in entries)
            {
                Assert.DoesNotContain("SECRETGROUP", entry);
                Assert.DoesNotContain("250", entry);
                Assert.DoesNotContain("EURUSD", entry);
                Assert.DoesNotContain("password", entry, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("login", entry, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static async Task<List<string>> LoggedReadAsync(int deals)
        {
            var capture = new CapturingLoggerFactory();
            var channel = new FakeStreamingChannel();
            channel.History.AddRange(MakeHistory(deals));
            using var client = ClientOver(channel, new Mt5GrpcClientOptions { LoggerFactory = capture });

            await DrainAsync(client.StreamDealsAsync(WholeHistory()));

            return capture.Entries;
        }

        // --- T020: GetAllDealsAsync signature (contract section C; FR-004) -----

        [Fact]
        public void Get_all_deals_async_returns_the_same_result_type_as_get_deals_async()
        {
            var getAll = typeof(Mt5GrpcClient).GetMethod(nameof(Mt5GrpcClient.GetAllDealsAsync));
            var getDeals = typeof(Mt5GrpcClient).GetMethod(nameof(Mt5GrpcClient.GetDealsAsync));

            Assert.NotNull(getAll);
            Assert.NotNull(getDeals);

            // Identical return type is what makes migration a one-token rename.
            Assert.Equal(typeof(Task<Mt5GrpcResult<DealsResponse>>), getAll!.ReturnType);
            Assert.Equal(getDeals!.ReturnType, getAll.ReturnType);

            Assert.Equal(
                new[] { typeof(DealsRequest), typeof(DateTime?), typeof(CancellationToken) },
                getAll.GetParameters().Select(parameter => parameter.ParameterType));
            Assert.All(getAll.GetParameters(), parameter => Assert.True(parameter.IsOptional));
        }

        // --- T021: success (US3-AC1, US3-AC2; FR-003, FR-004; contract C1, C2) -

        [Fact]
        public async Task Get_all_deals_concatenates_every_chunk_in_order()
        {
            var channel = new FakeStreamingChannel();
            channel.History.AddRange(MakeHistory(1571));
            using var client = ClientOver(channel);

            var result = await client.GetAllDealsAsync(WholeHistory());
            var unary = await client.GetDealsAsync(WholeHistory());

            Assert.True(result.IsSuccess);
            Assert.Null(result.Error);
            Assert.NotNull(result.Value);
            Assert.Null(result.Value!.Error);

            // 500 + 500 + 500 + 71 chunks, appended in stream order.
            Assert.Equal(1571, result.Value.Deals.Count);
            Assert.Equal(
                channel.History.Select(deal => deal.Ticket),
                result.Value.Deals.Select(deal => deal.Ticket));

            // ...and identical to the unary read for the same filters.
            Assert.Equal(
                unary.Value!.Deals.Select(deal => deal.Ticket),
                result.Value.Deals.Select(deal => deal.Ticket));
            Assert.Equal(unary.Value.Deals, result.Value.Deals);
        }

        [Fact]
        public async Task Get_all_deals_returns_success_with_no_deals_for_an_empty_history()
        {
            var channel = new FakeStreamingChannel();
            channel.History.AddRange(MakeHistory(10));
            using var client = ClientOver(channel);

            var request = new DealsRequest { TimeFilter = new TimeFilter { DateFrom = 1, DateTo = 2 } };
            var result = await client.GetAllDealsAsync(request);

            Assert.True(result.IsSuccess);
            Assert.Null(result.Error);
            Assert.NotNull(result.Value);
            Assert.Empty(result.Value!.Deals);
            Assert.Null(result.Value.Error);
        }

        [Theory]
        [InlineData("time", false)]
        [InlineData("time", true)]
        [InlineData("ticket", false)]
        [InlineData("ticket", true)]
        [InlineData("position", false)]
        [InlineData("position", true)]
        public async Task Get_all_deals_equals_get_deals_for_every_filter_form(
            string filterForm, bool withGroup)
        {
            var channel = new FakeStreamingChannel();
            channel.History.AddRange(MakeParityHistory(1200));
            using var client = ClientOver(channel);

            var all = await client.GetAllDealsAsync(ParityRequest(filterForm, withGroup));
            var unary = await client.GetDealsAsync(ParityRequest(filterForm, withGroup));

            Assert.True(all.IsSuccess, all.Error?.Message);
            Assert.True(unary.IsSuccess, unary.Error?.Message);
            Assert.NotEmpty(all.Value!.Deals);
            Assert.Equal(unary.Value!.Deals.Count, all.Value.Deals.Count);
            Assert.Equal(
                unary.Value.Deals.Select(deal => deal.Ticket),
                all.Value.Deals.Select(deal => deal.Ticket));
        }

        // --- T022: failure (US3-AC3; FR-004; contract C3) ----------------------

        [Fact]
        public async Task Get_all_deals_reports_an_in_band_error_as_a_failure_result()
        {
            var channel = new FakeStreamingChannel
            {
                StreamScript = new FakeStreamScript().YieldError(-10005, "No history found"),
            };
            using var client = ClientOver(channel);

            var result = await client.GetAllDealsAsync(WholeHistory());

            Assert.False(result.IsSuccess);
            Assert.Null(result.Value);
            Assert.NotNull(result.Error);
            Assert.Equal(-10005, result.Error!.Mt5ErrorCode);
            Assert.Equal("No history found", result.Error.Message);
            Assert.Equal(StreamOperation, result.Error.Operation);
        }

        [Fact]
        public async Task Get_all_deals_reports_a_transport_fault_as_a_failure_result()
        {
            var channel = new FakeStreamingChannel
            {
                StreamScript = new FakeStreamScript()
                    .Fault(StatusCode.Unimplemented, "Method not found: StreamDeals"),
            };
            using var client = ClientOver(channel);

            var result = await client.GetAllDealsAsync(WholeHistory());

            Assert.False(result.IsSuccess);
            Assert.Null(result.Value);
            Assert.Equal(StatusCode.Unimplemented, result.Error!.StatusCode);

            // A drop-in for GetDealsAsync must not fall back to it either.
            Assert.Equal(0, channel.InvocationCount("GetDeals"));
        }

        [Fact]
        public async Task Get_all_deals_reports_cancellation_as_a_failure_result_without_throwing()
        {
            var channel = new FakeStreamingChannel
            {
                MessageDelay = TimeSpan.FromMilliseconds(40),
                StreamScript = ScriptOfChunks(200, dealsPerChunk: 5),
            };
            using var client = ClientOver(channel);
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(120));

            var result = await client.GetAllDealsAsync(WholeHistory(), null, cts.Token);

            Assert.False(result.IsSuccess);
            Assert.Null(result.Value);
            Assert.Equal(StatusCode.Cancelled, result.Error!.StatusCode);
            Assert.True(channel.WasDisposed, "the streaming call was not released on cancellation");
        }

        [Fact]
        public async Task Get_all_deals_discards_chunks_already_received_when_the_stream_faults()
        {
            var channel = new FakeStreamingChannel
            {
                // Three good chunks land before the fault: a caller must not be
                // handed those and mistake a truncated history for a complete one.
                StreamScript = new FakeStreamScript()
                    .YieldDeals(MakeHistory(500, firstTicket: 1))
                    .YieldDeals(MakeHistory(500, firstTicket: 501))
                    .YieldDeals(MakeHistory(500, firstTicket: 1001))
                    .Fault(StatusCode.Unavailable, "connection reset"),
            };
            using var client = ClientOver(channel);

            var result = await client.GetAllDealsAsync(WholeHistory());

            Assert.False(result.IsSuccess);
            Assert.Null(result.Value);
            Assert.Equal(StatusCode.Unavailable, result.Error!.StatusCode);
        }

        // --- T017: the net48 example proves the native-channel path (US2-AC1) --

        [Fact]
        public void NetFramework48_example_reads_deals_over_the_grpc_core_channel()
        {
            var program = File.ReadAllText(
                Path.Combine(ClientRoot(), "examples", "NetFramework48ClientExample", "Program.cs"));

            Assert.Contains("Mt5GrpcClientFactory.CreateCore", program);
            Assert.Contains("StreamDealsAsync", program);
        }

        // --- helpers ----------------------------------------------------------

        private static Mt5GrpcClient ClientOver(
            FakeStreamingChannel channel, Mt5GrpcClientOptions? options = null)
        {
            // ownsChannel: false so disposing the client does not shut the fake down.
            return new Mt5GrpcClient(channel, options ?? new Mt5GrpcClientOptions(), ownsChannel: false);
        }

        /// <summary>A time filter wide enough to select the whole fake history.</summary>
        private static DealsRequest WholeHistory()
        {
            return new DealsRequest { TimeFilter = new TimeFilter { DateFrom = 0, DateTo = 0 } };
        }

        private static DealsRequest ByTicket(ulong ticket)
        {
            return new DealsRequest { Ticket = ticket };
        }

        private static async Task<List<DealsResponse>> DrainAsync(IAsyncEnumerable<DealsResponse> stream)
        {
            var chunks = new List<DealsResponse>();
            await foreach (var chunk in stream)
            {
                chunks.Add(chunk);
            }

            return chunks;
        }

        private static IEnumerable<Deal> MakeHistory(int count, ulong firstTicket = 1)
        {
            for (var index = 0; index < count; index++)
            {
                var ticket = firstTicket + (ulong)index;
                yield return new Deal
                {
                    Ticket = ticket,
                    Order = ticket,
                    PositionId = ticket,
                    Symbol = "EURUSD",
                    Time = 1_700_000_000 + index,
                    TimeMsc = (1_700_000_000L + index) * 1000,
                    Volume = 0.1,
                    Price = 1.2345,
                };
            }
        }

        /// <summary>
        /// A history in which all three filter forms select a non-empty proper
        /// subset and <c>group</c> genuinely narrows the time-filtered read.
        /// </summary>
        private static IEnumerable<Deal> MakeParityHistory(int count)
        {
            for (var index = 0; index < count; index++)
            {
                yield return new Deal
                {
                    Ticket = 1_000UL + (ulong)index,
                    Order = 500UL + (ulong)(index % 3),
                    PositionId = 900UL + (ulong)(index % 5),
                    Symbol = index % 2 == 0 ? "EURUSD" : "GBPJPY",
                    Time = 1_700_000_000 + index,
                    TimeMsc = (1_700_000_000L + index) * 1000,
                    Volume = 0.1,
                    Price = 1.2345,
                };
            }
        }

        private static DealsRequest ParityRequest(string filterForm, bool withGroup)
        {
            var request = filterForm switch
            {
                "time" => new DealsRequest
                {
                    TimeFilter = new TimeFilter { DateFrom = 1_700_000_000, DateTo = 1_700_001_000 },
                },
                "ticket" => new DealsRequest { Ticket = 501 },
                "position" => new DealsRequest { Position = 902 },
                _ => throw new ArgumentOutOfRangeException(nameof(filterForm), filterForm, null),
            };

            if (withGroup)
            {
                request.Group = "EUR*";
            }

            return request;
        }

        private static FakeStreamScript ScriptOfChunks(int chunks, int dealsPerChunk)
        {
            var script = new FakeStreamScript();
            for (var index = 0; index < chunks; index++)
            {
                script.YieldDeals(MakeHistory(dealsPerChunk, firstTicket: (ulong)(index * dealsPerChunk) + 1));
            }

            return script;
        }

        private static string ClientRoot()
        {
            return Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        }

        /// <summary>
        /// Minimal log capture. <c>TestLoggerProvider</c> lives in the Tests project
        /// and is not visible here, and adding a project reference for it would be a
        /// heavier coupling than this handful of lines.
        /// </summary>
        private sealed class CapturingLoggerFactory : ILoggerFactory
        {
            private readonly ConcurrentQueue<string> entries = new ConcurrentQueue<string>();

            public List<string> Entries
            {
                get { return entries.ToList(); }
            }

            public ILogger CreateLogger(string categoryName)
            {
                return new CapturingLogger(entries);
            }

            public void AddProvider(ILoggerProvider provider)
            {
            }

            public void Dispose()
            {
            }

            private sealed class CapturingLogger : ILogger
            {
                private readonly ConcurrentQueue<string> entries;

                public CapturingLogger(ConcurrentQueue<string> entries)
                {
                    this.entries = entries;
                }

                public IDisposable? BeginScope<TState>(TState state)
                    where TState : notnull
                {
                    return null;
                }

                public bool IsEnabled(LogLevel logLevel)
                {
                    return true;
                }

                public void Log<TState>(
                    LogLevel logLevel,
                    EventId eventId,
                    TState state,
                    Exception? exception,
                    Func<TState, Exception?, string> formatter)
                {
                    entries.Enqueue(formatter(state, exception));
                }
            }
        }
    }
}
