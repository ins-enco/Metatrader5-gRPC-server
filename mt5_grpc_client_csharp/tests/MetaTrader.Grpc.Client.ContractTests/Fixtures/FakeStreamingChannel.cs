using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Metatrader.V1;

namespace MetaTrader.Grpc.Client.ContractTests.Fixtures
{
    /// <summary>
    /// A <see cref="ChannelBase"/> test double that serves <c>TradeHistoryService</c>
    /// calls from an in-memory deal list, with no socket, no live broker and no
    /// package reference beyond <c>Grpc.Core.Api</c>.
    ///
    /// It intercepts at the <see cref="CallInvoker"/> boundary, so the real
    /// generated stub, the real <c>Mt5GrpcStreamingInvoker</c> and the real public
    /// <c>Mt5GrpcClient</c> methods all run under test. Construct the client with
    /// the internal <c>Mt5GrpcClient(ChannelBase, Mt5GrpcClientOptions, bool
    /// ownsChannel)</c> constructor passing <c>ownsChannel: false</c>, so disposing
    /// the client does not shut the fake down.
    ///
    /// Capabilities, in the order the streaming contract needs them:
    /// <list type="bullet">
    /// <item>serves <see cref="AsyncServerStreamingCall{T}"/> from a scripted or
    /// history-derived chunk sequence;</item>
    /// <item>serves <see cref="AsyncUnaryCall{T}"/> from the <em>same</em>
    /// underlying list, which is what makes unary/streaming parity meaningful;</item>
    /// <item>captures every transmitted <see cref="DealsRequest"/> and the invoked
    /// method name (see <see cref="Calls"/>), the only way to assert verbatim
    /// chunk-size transmission;</item>
    /// <item>exposes a per-call <see cref="FakeCallRecord.WasDisposed"/> probe fed
    /// by the call's <c>disposeAction</c>;</item>
    /// <item>emulates the server's own chunk policy - default 500, cap 1000, a
    /// transmitted 0 treated as unset - entirely on this side of the wire;</item>
    /// <item>can be scripted to fault with a chosen <see cref="RpcException"/> or to
    /// yield an error-only <see cref="DealsResponse"/>.</item>
    /// </list>
    /// </summary>
    internal sealed class FakeStreamingChannel : ChannelBase
    {
        /// <summary>Server-side default when <c>chunk_size</c> is unset or zero.</summary>
        public const int DefaultChunkSize = 500;

        /// <summary>Server-side ceiling; a larger requested value is clamped to this.</summary>
        public const int MaxChunkSize = 1000;

        private readonly List<FakeCallRecord> calls = new List<FakeCallRecord>();
        private readonly object gate = new object();

        public FakeStreamingChannel()
            : base("fake.invalid:0")
        {
        }

        /// <summary>
        /// The whole closed history both RPCs read from. Filters are applied over
        /// this list identically on the unary and streaming paths.
        /// </summary>
        public List<Deal> History { get; } = new List<Deal>();

        /// <summary>
        /// Delay before each streamed message. Non-zero gives a test a window in
        /// which to cancel the token or let a deadline elapse mid-stream.
        /// </summary>
        public TimeSpan MessageDelay { get; set; }

        /// <summary>
        /// Script applied to every streaming call. When null the channel derives
        /// chunks from <see cref="History"/> using the server's chunk policy.
        /// </summary>
        public FakeStreamScript? StreamScript { get; set; }

        /// <summary>
        /// Per-call script selection, for tests that run two concurrent streams
        /// with different behaviour. Takes precedence over <see cref="StreamScript"/>;
        /// returning null falls through to history-derived chunking. Keying off the
        /// request (for example its <c>Ticket</c> or <c>ChunkSize</c>) keeps the
        /// assignment deterministic regardless of enumeration interleaving.
        /// </summary>
        public Func<DealsRequest, FakeStreamScript?>? StreamScriptSelector { get; set; }

        /// <summary>Fault every unary call with this exception instead of answering it.</summary>
        public RpcException? UnaryFault { get; set; }

        /// <summary>Answer every unary call with this error-only response.</summary>
        public Error? UnaryError { get; set; }

        /// <summary>Every intercepted call, oldest first, request and options captured.</summary>
        public IReadOnlyList<FakeCallRecord> Calls
        {
            get { lock (gate) { return calls.ToArray(); } }
        }

        /// <summary>The most recent intercepted call, or null when none was made.</summary>
        public FakeCallRecord? LastCall
        {
            get { lock (gate) { return calls.Count == 0 ? null : calls[calls.Count - 1]; } }
        }

        /// <summary>True once any intercepted call has been disposed.</summary>
        public bool WasDisposed
        {
            get { return Calls.Any(call => call.WasDisposed); }
        }

        /// <summary>How many times the named RPC was invoked (for example "GetDeals").</summary>
        public int InvocationCount(string method)
        {
            return Calls.Count(call => call.Method == method);
        }

        /// <summary>The most recent captured request for the named RPC.</summary>
        public DealsRequest CapturedRequest(string method)
        {
            return Calls.Last(call => call.Method == method).Request;
        }

        public override CallInvoker CreateCallInvoker()
        {
            return new FakeCallInvoker(this);
        }

        // --- server emulation -------------------------------------------------

        /// <summary>
        /// The server's <c>_chunk_size_for</c>: an unset or zero <c>chunk_size</c>
        /// takes the default, anything larger than the cap is clamped to it. The
        /// client is not involved in any of this - it only transmits what the
        /// caller set.
        /// </summary>
        public static int EffectiveChunkSize(DealsRequest request)
        {
            if (request.HasChunkSize && request.ChunkSize > 0)
            {
                return Math.Min((int)request.ChunkSize, MaxChunkSize);
            }

            return DefaultChunkSize;
        }

        /// <summary>
        /// The server's <c>_fetch_deals</c> filter selection, faithful enough that
        /// both RPCs necessarily agree: the time form honours <c>group</c>, the
        /// ticket and position forms ignore it, and no active form yields the
        /// server's <c>-2</c> error rather than a client-side rejection. Returns
        /// null for "no valid filter criteria".
        /// </summary>
        public IReadOnlyList<Deal>? Filtered(DealsRequest request)
        {
            switch (request.FilterCase)
            {
                case DealsRequest.FilterOneofCase.TimeFilter:
                    var from = request.TimeFilter.DateFrom;
                    var to = request.TimeFilter.DateTo;
                    return History
                        .Where(deal => (from == 0 || deal.Time >= from) && (to == 0 || deal.Time <= to))
                        .Where(deal => MatchesGroup(deal.Symbol, request.HasGroup ? request.Group : "*"))
                        .ToList();

                case DealsRequest.FilterOneofCase.Ticket:
                    return History.Where(deal => deal.Order == request.Ticket).ToList();

                case DealsRequest.FilterOneofCase.Position:
                    return History.Where(deal => deal.PositionId == request.Position).ToList();

                default:
                    return null;
            }
        }

        /// <summary>The server's "No valid filter criteria provided" outcome.</summary>
        public static Error NoFilterError()
        {
            return new Error { Code = -2, Message = "No valid filter criteria provided" };
        }

        private static bool MatchesGroup(string symbol, string group)
        {
            if (string.IsNullOrEmpty(group) || group == "*")
            {
                return true;
            }

            var startsWildcard = group.StartsWith("*", StringComparison.Ordinal);
            var endsWildcard = group.EndsWith("*", StringComparison.Ordinal);
            var core = group.Trim('*');

            if (startsWildcard && endsWildcard)
            {
                return symbol.IndexOf(core, StringComparison.Ordinal) >= 0;
            }

            if (endsWildcard)
            {
                return symbol.StartsWith(core, StringComparison.Ordinal);
            }

            if (startsWildcard)
            {
                return symbol.EndsWith(core, StringComparison.Ordinal);
            }

            return string.Equals(symbol, group, StringComparison.Ordinal);
        }

        // --- call plumbing ----------------------------------------------------

        private FakeCallRecord Record(string service, string method, DealsRequest request, CallOptions options)
        {
            var record = new FakeCallRecord(service, method, request, options);
            lock (gate)
            {
                calls.Add(record);
            }

            return record;
        }

        private AsyncServerStreamingCall<DealsResponse> CreateStreamingCall(
            string service, string method, DealsRequest request, CallOptions options)
        {
            var record = Record(service, method, request, options);
            var script = StreamScriptSelector?.Invoke(request) ?? StreamScript ?? ScriptFromHistory(request);
            var reader = new ScriptedStreamReader(script, MessageDelay, options.Deadline, record);

            return new AsyncServerStreamingCall<DealsResponse>(
                reader,
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => record.WasDisposed = true);
        }

        private AsyncUnaryCall<DealsResponse> CreateUnaryCall(
            string service, string method, DealsRequest request, CallOptions options)
        {
            var record = Record(service, method, request, options);

            Task<DealsResponse> responseTask;
            if (UnaryFault != null)
            {
                responseTask = Task.FromException<DealsResponse>(UnaryFault);
            }
            else if (UnaryError != null)
            {
                responseTask = Task.FromResult(new DealsResponse { Error = UnaryError });
            }
            else
            {
                var filtered = Filtered(request);
                var response = new DealsResponse();
                if (filtered == null)
                {
                    response.Error = NoFilterError();
                }
                else
                {
                    response.Deals.AddRange(filtered);
                }

                record.MessagesRead++;
                responseTask = Task.FromResult(response);
            }

            return new AsyncUnaryCall<DealsResponse>(
                responseTask,
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => record.WasDisposed = true);
        }

        /// <summary>
        /// Chunk <see cref="History"/> exactly as the server would for this request:
        /// filtered, sliced by the effective chunk size, zero messages for an empty
        /// history, one error-only message when no filter form is active.
        /// </summary>
        private FakeStreamScript ScriptFromHistory(DealsRequest request)
        {
            var script = new FakeStreamScript();
            var filtered = Filtered(request);

            if (filtered == null)
            {
                return script.Yield(new DealsResponse { Error = NoFilterError() });
            }

            var chunkSize = EffectiveChunkSize(request);
            for (var offset = 0; offset < filtered.Count; offset += chunkSize)
            {
                script.YieldDeals(filtered.Skip(offset).Take(chunkSize));
            }

            return script;
        }

        private sealed class FakeCallInvoker : CallInvoker
        {
            private readonly FakeStreamingChannel channel;

            public FakeCallInvoker(FakeStreamingChannel channel)
            {
                this.channel = channel;
            }

            public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
                Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
            {
                var call = channel.CreateStreamingCall(
                    method.ServiceName, method.Name, Cast(request), options);
                return (AsyncServerStreamingCall<TResponse>)(object)call;
            }

            public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
                Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
            {
                var call = channel.CreateUnaryCall(
                    method.ServiceName, method.Name, Cast(request), options);
                return (AsyncUnaryCall<TResponse>)(object)call;
            }

            public override TResponse BlockingUnaryCall<TRequest, TResponse>(
                Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
            {
                throw new NotSupportedException("The fake channel serves async calls only.");
            }

            public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
                Method<TRequest, TResponse> method, string? host, CallOptions options)
            {
                throw new NotSupportedException("The contract has no client-streaming RPC.");
            }

            public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
                Method<TRequest, TResponse> method, string? host, CallOptions options)
            {
                throw new NotSupportedException("The contract has no bidirectional-streaming RPC.");
            }

            private static DealsRequest Cast<TRequest>(TRequest request)
            {
                // The fake serves TradeHistoryService only, so the request is always
                // a DealsRequest - and it is the caller's own instance, unmodified,
                // which is exactly what the transmission assertions inspect.
                return request as DealsRequest
                    ?? throw new NotSupportedException(
                        "The fake channel serves DealsRequest only, got " + typeof(TRequest).Name + ".");
            }
        }

        private sealed class ScriptedStreamReader : IAsyncStreamReader<DealsResponse>
        {
            private readonly IReadOnlyList<Func<DealsResponse>> steps;
            private readonly TimeSpan delay;
            private readonly DateTime? deadline;
            private readonly FakeCallRecord record;
            private int index = -1;

            public ScriptedStreamReader(
                FakeStreamScript script, TimeSpan delay, DateTime? deadline, FakeCallRecord record)
            {
                steps = script.Steps;
                this.delay = delay;
                this.deadline = deadline;
                this.record = record;
            }

            public DealsResponse Current { get; private set; } = null!;

            public async Task<bool> MoveNext(CancellationToken cancellationToken)
            {
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Yield();
                }

                // A real transport surfaces an elapsed deadline as this status; the
                // client maps it, and must not pre-empt it.
                if (deadline.HasValue && DateTime.UtcNow >= deadline.Value)
                {
                    throw new RpcException(new Status(StatusCode.DeadlineExceeded, "Deadline Exceeded"));
                }

                index++;
                if (index >= steps.Count)
                {
                    Current = null!;
                    return false;
                }

                Current = steps[index]();
                record.MessagesRead++;
                return true;
            }
        }
    }

    /// <summary>
    /// One intercepted call: which RPC, the caller's request instance exactly as
    /// transmitted, the call options, and whether the call was released.
    /// </summary>
    internal sealed class FakeCallRecord
    {
        internal FakeCallRecord(string service, string method, DealsRequest request, CallOptions options)
        {
            Service = service;
            Method = method;
            Request = request;
            Options = options;
        }

        public string Service { get; }

        public string Method { get; }

        /// <summary>The transmitted request, by reference - not a copy and not rebuilt.</summary>
        public DealsRequest Request { get; }

        public CallOptions Options { get; }

        /// <summary>Set by the call's <c>disposeAction</c> when the call is released.</summary>
        public bool WasDisposed { get; internal set; }

        public int MessagesRead { get; internal set; }
    }

    /// <summary>
    /// A scripted server-streaming response: an ordered list of steps, each either
    /// yielding a message or faulting the call.
    /// </summary>
    internal sealed class FakeStreamScript
    {
        private readonly List<Func<DealsResponse>> steps = new List<Func<DealsResponse>>();

        internal IReadOnlyList<Func<DealsResponse>> Steps
        {
            get { return steps; }
        }

        /// <summary>Yield this message next.</summary>
        public FakeStreamScript Yield(DealsResponse response)
        {
            steps.Add(() => response);
            return this;
        }

        /// <summary>Yield a data chunk holding these deals.</summary>
        public FakeStreamScript YieldDeals(IEnumerable<Deal> deals)
        {
            var response = new DealsResponse();
            response.Deals.AddRange(deals);
            return Yield(response);
        }

        /// <summary>Yield a terminal error-only message (in-band MT5 failure).</summary>
        public FakeStreamScript YieldError(int code, string message)
        {
            return Yield(new DealsResponse { Error = new Error { Code = code, Message = message } });
        }

        /// <summary>Fault the call at this point with a transport status.</summary>
        public FakeStreamScript Fault(RpcException exception)
        {
            steps.Add(() => throw exception);
            return this;
        }

        /// <summary>Fault the call at this point with the given status code.</summary>
        public FakeStreamScript Fault(StatusCode statusCode, string detail)
        {
            return Fault(new RpcException(new Status(statusCode, detail)));
        }
    }
}
