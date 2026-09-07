# Phase 0 Research: C# Client StreamDeals Surface

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Date**: 2026-09-07

The spec carried no `[NEEDS CLARIFICATION]` markers (the one that existed was resolved to
Option A before planning). Phase 0 therefore resolves the spec's four explicitly-recorded
**Assumptions to confirm** — public surface names, the convenience surface's failure
behaviour, the absence of client-side chunk-size policy, and "no new test
infrastructure" — plus the two release questions. Each decision below was checked against
the code as it exists on this branch, not against convention.

---

## Decision 1 — No proto, generated-code or server behaviour change is needed

**Decision**: Touch no `.proto` file, no generated binding, and no server implementation.
Consume the existing contract through bindings that `Grpc.Tools` regenerates at build
time.

**Rationale**: Verified in the tree:

- `protos/deal.proto` already declares
  `rpc StreamDeals(DealsRequest) returns (stream DealsResponse)` beside `GetDeals`, and
  `optional uint32 chunk_size = 5` on `DealsRequest`, documented as "StreamDeals only …
  Defaults to 500, capped at 1000. Ignored by GetDeals."
- `mt5_grpc_server/mt5_grpc_server/imp/deals_history.py` implements `StreamDeals`,
  sharing filter logic with `GetDeals` through `_fetch_deals`, with
  `DEFAULT_CHUNK_SIZE = 500`, `MAX_CHUNK_SIZE = 1000`, `_chunk_size_for` clamping, an
  `is_active()` check per chunk, and error-only output on failure.
- `MetaTrader.Grpc.Client.csproj` compiles `<Protobuf Include="..\..\..\protos\*.proto"
  GrpcServices="Client" />`, so `TradeHistoryServiceClient.StreamDeals` and
  `DealsRequest.ChunkSize` / `HasChunkSize` / `ClearChunkSize` appear on the next build
  with zero authored contract work.

**Alternatives considered**: Vendoring a C# copy of the proto (rejected — the constitution
makes `protos/` the single source of truth and `check-generated.ps1` exists to prove the
build regenerates from it); adding a server-side size guard so `GetDeals` cannot abort
(rejected — explicitly out of scope per the spec, and it is a server-resilience concern
rather than a way to give C# callers a safe path).

---

## Decision 2 — Public surface names: `StreamDealsAsync` and `GetAllDealsAsync`

**Decision**:

```csharp
IAsyncEnumerable<DealsResponse> StreamDealsAsync(
    DealsRequest? request = null, DateTime? deadline = null, CancellationToken cancellationToken = default);

Task<Mt5GrpcResult<DealsResponse>> GetAllDealsAsync(
    DealsRequest? request = null, DateTime? deadline = null, CancellationToken cancellationToken = default);
```

**Rationale**: Names are a published-package commitment, so they follow the two patterns
already in the library rather than inventing a third.

- The chunk-level name mirrors the RPC exactly, as `SubscribeTradeTransactionsAsync`
  mirrors `SubscribeTradeTransactions`. A reader who greps the proto finds the method.
- The convenience name reads as a whole-history variant of the existing operation, and its
  return type is *identical* to `GetDealsAsync`'s
  (`Task<Mt5GrpcResult<DealsResponse>>`), which makes migration a single-token rename:
  `GetDealsAsync` → `GetAllDealsAsync` leaves `result.IsSuccess`, `result.Error` and
  `result.Value.Deals` working unchanged. That is what "near drop-in" (FR-004) has to
  mean in practice.
- Parameter order and optionality copy every other operation in
  `Mt5GrpcClient.Trading.cs` (`request`, `deadline`, `cancellationToken`, all optional),
  so the two new methods are indistinguishable in shape from the 11 existing ones.

**Alternatives considered**: `GetDealsStreamAsync` (rejected — reads as "get a stream
object", and diverges from the RPC name); `GetDealsChunkedAsync` (rejected — names the
mechanism, not the result); returning `Task<Mt5GrpcResult<IReadOnlyList<Deal>>>` from the
convenience surface (rejected — a different result type forces every migrating caller to
rewrite the response-handling code the drop-in requirement exists to preserve);
`ReadAllDealsAsync` (rejected — `Get*` is the established prefix for every read in this
library).

---

## Decision 3 — Test seam: fake `ChannelBase` + `CallInvoker`, not a real in-process server

**Decision**: Drive streaming behaviour through test doubles that intercept the call at the
`CallInvoker` boundary and return canned `AsyncServerStreamingCall<DealsResponse>` /
`AsyncUnaryCall<DealsResponse>` objects, constructing `Mt5GrpcClient` through its
`internal` constructor.

**Rationale**: The spec's assumption that "the existing in-process test-server and
streaming fixtures are sufficient" does not survive contact with the code, so it is
recorded here as corrected rather than silently worked around:

- `tests/.../Fixtures/GrpcTestServerFixture.cs` hosts nothing — it exposes an `Address`
  property and an empty `Dispose`.
- `ContractTests` references `Grpc.Core.Api` only (no `Grpc.Core` native server, no
  `Grpc.AspNetCore.Server`), so there is nothing available to host `streaming_fixture.proto`
  even though it is generated with `GrpcServices="Both"`.
- The existing streaming tests (`TradeEventsStreamingTests`) therefore assert against
  hand-rolled `IAsyncEnumerable` helpers, which never exercise the real invoker.

The chosen seam is available with the packages already referenced: `ChannelBase` is
abstract over `CreateCallInvoker()`, generated `ClientBase<T>` resolves every call through
that invoker, `CallInvoker.AsyncServerStreamingCall(...)` receives the request object, and
`AsyncServerStreamingCall<T>` has a public constructor taking an `IAsyncStreamReader<T>`
and a `disposeAction`. That yields, with no socket and no new dependency:

1. real `Mt5GrpcClient.StreamDealsAsync` / `GetAllDealsAsync` under test, not a stand-in;
2. the real `Mt5GrpcStreamingInvoker` error mapping, logging and disposal path;
3. **request capture**, the only way to assert FR-005 verbatim transmission (including
   `HasChunkSize == false` when the caller set nothing);
4. a `disposeAction` probe, the only way to assert FR-008 "the call is released" on
   cancellation and on early `break`;
5. one fake serving both the unary and streaming methods from the same deal list, which is
   what makes the FR-007 parity assertion meaningful.

`InternalsVisibleTo` already grants `MetaTrader.Grpc.Client.ContractTests` and
`MetaTrader.Grpc.Client.Tests` access to the `internal Mt5GrpcClient(ChannelBase, options,
bool ownsChannel)` constructor, so no production visibility change is required. Construct
with `ownsChannel: false` so `Dispose` does not call `ShutdownAsync` on the fake.

**Alternatives considered**: Adding `Grpc.Core` 2.46.6 as a test-only server to host a real
localhost stream (rejected — pulls a deprecated native dependency into the test graph,
binds a port in CI, and still cannot assert what the client transmitted); an ASP.NET Core
in-process `TestServer` with `Grpc.AspNetCore.Server` (rejected — a heavier new dependency
whose end-to-end value is already covered by the production evidence in the spec and by
manual verification in `quickstart.md`); continuing with hand-rolled `IAsyncEnumerable`
sequences as `TradeEventsStreamingTests` does (rejected — it would leave the actual
production code path, the invoker plus generated stub, untested for the feature whose
entire purpose is to change which RPC gets called).

---

## Decision 4 — `Mt5GrpcStreamingInvoker` is reused with no modification

**Decision**: Route `StreamDealsAsync` through the existing invoker; change nothing in
`Mt5GrpcStreamingInvoker.cs`.

**Rationale**: Reading the invoker against this feature's requirements, every behaviour is
already the required one:

| Requirement | Existing invoker behaviour |
| --- | --- |
| FR-002 in-band error terminates enumeration | `FromMt5Error(errorSelector(current))`, logs `Mt5ErrorPayload`, throws `Mt5GrpcClientException` |
| FR-002 transport fault terminates enumeration | catches `RpcException`, logs `CallFailure`, throws with the mapped error |
| FR-003 empty stream completes normally | `MoveNext` returns false on the first call, `yield break`, zero items, no error |
| FR-008 cancellation / deadline | `[EnumeratorCancellation]` token passed to `MoveNext`; `OperationCanceledException` mapped through `FromCancellation`; `DeadlineExceeded` logged |
| FR-008 release on abandon | `using var streamingCall` inside the async iterator disposes the call when the enumerator is disposed, including on `break` |
| FR-010 bounded logging | one log per failure event, none per message, no payload or credential emitted |

Adding a deal-count or chunk-index log line was considered and rejected: it would grow
output with history size, which FR-010 forbids.

**Consequence worth stating**: the production diff for User Stories 1 and 3 is two methods
in one file. Anything larger should be treated as a signal that the design drifted.

---

## Decision 5 — Convenience surface: concatenate into one `DealsResponse`, discard partials, never raise

**Decision**: `GetAllDealsAsync` enumerates `StreamDealsAsync` internally, appends every
chunk's `Deals` into one freshly-constructed `DealsResponse`, and returns
`Mt5GrpcResult<DealsResponse>.Success(...)`. On any failure it returns
`Mt5GrpcResult<DealsResponse>.Failure(mappedError)` with **no** partial collection and
without throwing — matching `Mt5GrpcUnaryInvoker`'s contract, including its
`Mt5GrpcClientException` / `OperationCanceledException` / `Exception` catch ladder mapped
through `FromCancellation` and `FromException`.

**Rationale**: `GetDealsAsync` never throws for a call or MT5 failure; it returns a failure
result. A drop-in replacement that throws would break every migrating caller's control
flow, so the exception the chunk-level surface raises must be absorbed here. Returning a
*partial* collection alongside an error is worse than returning none: a caller who checks
`Value` before `Error` — or logs the count — would silently treat a truncated history as
complete, which for reconciliation work is a correctness bug, not an inconvenience.
Callers who genuinely want partial progress have the chunk-level surface, and the
documentation says so at the point of use.

An empty history returns `Success` with `Deals.Count == 0` (never a failure): the
synthesized response object is non-null, which is what `Mt5GrpcResult<T>.Success` requires.

**Alternatives considered**: `Task<Mt5GrpcResult<IReadOnlyList<Deal>>>` (rejected — see
Decision 2); returning success-with-partials plus a populated `Error` (rejected as above,
and it contradicts `Mt5GrpcResult`'s invariant that `IsSuccess == (Error == null)`);
letting the exception propagate and documenting it (rejected — defeats the drop-in
requirement).

---

## Decision 6 — No client-side chunk-size policy, and no fallback to `GetDeals`

**Decision**: Forward the caller's `DealsRequest` unmodified. The client never sets,
raises, lowers, clamps, defaults or clears `ChunkSize`, and never falls back from
`StreamDeals` to `GetDeals` (or the reverse) for any status, including `Unimplemented`.

**Rationale**:

- `chunk_size` is proto3 `optional`, so presence is meaningful. Forwarding the caller's
  object as-is (exactly as `GetDealsAsync` does with `request ?? new DealsRequest()`) is
  the only way "unset stays unset" holds, and it keeps the server the single owner of the
  500 default and 1000 cap. A client-side default would fork policy across languages the
  moment the server's default changes.
- An automatic fallback to `GetDeals` on `Unimplemented` (a pre-`0.4.0` server) would send
  exactly the oversized single response this feature exists to avoid — turning a clear,
  actionable error into a server crash. The mapped `Unimplemented` error is the correct
  outcome, and FR-015's version gate is what lets a consumer avoid it in the first place.
- The cap deserves a warning rather than enforcement: at the spec's ~77 bytes/deal, 1000
  deals is ~77 KB, between the largest observed safe response (~75 KB) and the smallest
  observed fatal one (~121 KB). The honest position is documentation — "the cap is inside
  the uncertain band, prefer the default" — not a client-side ceiling that silently
  contradicts the server.

**Alternatives considered**: defaulting `ChunkSize` to 500 client-side (rejected —
duplicates server policy and breaks presence semantics); clamping to a client maximum
below the server cap (rejected — the client would report a chunk size the caller did not
ask for, and FR-005 forbids it); throwing on a chunk size above the cap (rejected — the
server clamps and the client must report server behaviour, not pre-empt it).

---

## Decision 7 — Release as `0.4.0` with the existing tooling, and cover the RPC with one server test

**Decision**: Run `python bump_version.py minor` (updates `mt5_grpc_server/setup.py` and
`mt5_grpc_proto/setup.py` together, `0.3.0` → `0.4.0`), add a root `CHANGELOG.md` `[0.4.0]`
entry naming `StreamDeals` and `chunk_size` as the additive contract change, set the client
to `5.1.0` with `<ProtoContractIdentity>protos-007-stream-deals</ProtoContractIdentity>`
and `<TestedServerVersionRange>[0.4.0,1.0.0)</TestedServerVersionRange>`, and quote both
values verbatim in the packed README and `<PackageReleaseNotes>`. Additionally add one
test-only `pytest` file for the server RPC.

**Rationale**:

- Both `setup.py` files still read `0.3.0` although `StreamDeals` landed after `0.3.0`
  shipped, so the current `[0.3.0,1.0.0)` range would advertise the RPC on servers that
  do not implement it. `0.4.0` is the first version whose contract contains it; the client
  narrowing its floor to `0.4.0` is what makes SC-009 checkable from metadata alone.
- `bump_version.py` is the repository's own tool and edits both packages in one pass, which
  is what "reproducible from repository scripts" requires (FR-016, Principle V).
- Two independent guards already enforce the metadata quoting —
  `DocumentationAccuracyTests.Readme_and_release_notes_carry_current_compatibility_metadata`
  and `check-package-metadata.ps1` — so the README header (currently "…`5.0.0`, with proto
  contract identity `protos-005-trade-transaction-events` … `[0.3.0,1.0.0)`") must be
  updated in the same change or the build fails. `check-package-metadata.ps1` also asserts
  an exact per-target dependency set, which stays green precisely because no dependency is
  added.
- **The server test is a widening beyond the spec's FR list, stated plainly**: commit
  95e3317 shipped `StreamDeals` with no `pytest` (`mt5_grpc_server/tests/` holds only
  `test_logging_interceptor.py`, `test_trade_action_validation.py`,
  `test_trade_events_stream.py`). Publishing `0.4.0` is the act that advertises the RPC,
  and Principle V requires a no-live-broker test path for new RPC behaviour. One mock-MT5
  file covering chunk boundaries, `_chunk_size_for` clamping (unset → 500, above cap →
  1000, zero → 500), an empty history yielding zero messages, and error-only output closes
  that gap for the cost of a single test file and changes no behaviour. It is separable: if
  it is cut, the client feature is unaffected and this plan's Constitution Check still
  passes on client coverage — but the release goes out untested.

**Alternatives considered**: keeping `0.3.0` and documenting "requires a build after commit
95e3317" (rejected — the spec's Option A decision, and a commit hash is not something a
consumer's package metadata can check); publishing the client's `5.1.0` first and the
Python `0.4.0` later (rejected as a *publish* order — FR-016 forbids advertising
`[0.4.0,1.0.0)` before a satisfying release exists; client work may still be completed and
verified beforehand); a `major` bump to `1.0.0` (rejected — the change is additive and
backward compatible).

---

## Observations outside this feature's scope

Recorded because they were found while reading the code, not proposed as work here:

1. **Publish-tag mismatch.** `mt5_grpc_client_csharp/README.md` documents tagging
   `csharp-client-v<X.Y.Z>` and calls the workflow "tags `csharp-client-v*`", but
   `.github/workflows/csharp-client-publish.yml` triggers on `v*.*.*` — which its own
   comment notes is shared with the server's Docker release. Following the README today
   publishes nothing. Worth a separate fix; it will matter when `5.1.0` ships.
2. **Credentials committed in the .NET Framework example.**
   `examples/NetFramework48ClientExample/Program.cs` carries a hard-coded login, password
   and broker address as `Default*` constants. The examples are edited by this feature, so
   the constants will be in the diff's neighbourhood; replacing them with
   argument-or-environment lookups is a small, separate change and should not be folded in
   silently.
3. **Uncommitted local edits to both examples.** `git status` shows working-tree changes in
   both `Program.cs` files (a changed server port, and ad-hoc `ads` / `d1` / `a` / `a2`
   locals in the netstandard example) that look like debugging scratch rather than intended
   content. They should be resolved before the example edits for FR-011 land on top of
   them.
