---

description: "Task list for C# Client StreamDeals Surface"
---

# Tasks: C# Client StreamDeals Surface

**Input**: Design documents from `/specs/007-csharp-stream-deals/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/client-surface.md](./contracts/client-surface.md), [quickstart.md](./quickstart.md)

**Tests**: **Included and required.** This feature changes a published package's public
behaviour, consumes a contract through generated bindings, and changes packaging and
documentation examples — all four triggers in the template's test rule, and FR-012/FR-013
mandate specific coverage. Tests use the fake-channel seam from
[research.md](./research.md) Decision 3: no live broker, no MT5 terminal, no Wine, no
socket, no new package reference.

**Organization**: Tasks are grouped by user story so each story can be implemented,
tested and shipped independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3, US4)
- Exact file paths are given in every task

## Path Conventions

Multi-language repo per [plan.md](./plan.md):

- C# client library: `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/`
- C# tests: `mt5_grpc_client_csharp/tests/{Tests,ContractTests,CompatibilityTests}/`
- C# examples: `mt5_grpc_client_csharp/examples/{NetStandardClientExample,NetFramework48ClientExample}/`
- Python server: `mt5_grpc_server/`, Python proto package: `mt5_grpc_proto/`
- Canonical protos: `protos/` — **not modified by this feature**

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Clear the working tree and materialise the generated bindings this feature consumes

- [ ] T001 Resolve the uncommitted working-tree changes in `mt5_grpc_client_csharp/examples/NetStandardClientExample/Program.cs` and `mt5_grpc_client_csharp/examples/NetFramework48ClientExample/Program.cs` (changed server port, ad-hoc `ads`/`d1`/`a`/`a2` locals) — commit them or revert them, so the FR-011 example edits in Phase 6 do not land on top of debugging scratch. See [research.md](./research.md) Observation 3.
- [ ] T002 Run `dotnet restore mt5_grpc_client_csharp/MetaTrader.Grpc.Client.sln` then `dotnet build mt5_grpc_client_csharp/MetaTrader.Grpc.Client.sln -c Debug` and confirm the regenerated `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/obj/Debug/*/DealGrpc.cs` now contains a `StreamDeals` method on `TradeHistoryServiceClient` and that `Deal.cs` exposes `DealsRequest.ChunkSize`/`HasChunkSize`/`ClearChunkSize`. The checked-in `obj/` output predates the proto change and has `GetDeals` only; no authored contract work is required, only this rebuild.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The test seam and contract inventory every user story below depends on

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [ ] T003 Create the fake-channel test seam in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/Fixtures/FakeStreamingChannel.cs`: a `ChannelBase` subclass whose `CreateCallInvoker()` returns a `CallInvoker` double that (a) serves `AsyncServerStreamingCall<DealsResponse>` from a scripted chunk list using the public `AsyncServerStreamingCall<T>(IAsyncStreamReader<T>, Task<Metadata>, Func<Status>, Func<Metadata>, Action disposeAction)` constructor, (b) serves `AsyncUnaryCall<DealsResponse>` from the same underlying deal list so unary/streaming parity can be asserted, (c) **captures the transmitted `DealsRequest`** and the invoked method name, (d) exposes a `WasDisposed` probe fed by `disposeAction`, (e) can emulate the server's documented chunk-size clamp (default 500, cap 1000) without the client's involvement, and (f) can be scripted to throw a chosen `RpcException` (including `StatusCode.Unimplemented`) or yield an error-only `DealsResponse`. Construct the client under test with the `internal Mt5GrpcClient(ChannelBase, Mt5GrpcClientOptions, bool ownsChannel: false)` constructor, already visible via `InternalsVisibleTo`. Verified available in `Grpc.Core.Api` 2.71.0 — no new package reference.
- [ ] T004 [P] Register the streaming RPC in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/ProtoContractCatalog.cs`: add `["TradeHistoryService"] = new[] { "StreamDeals" }` to `StreamingServices`, leaving `UnaryServices` untouched so `TradeHistoryService` keeps `GetDeals` and `GeneratedSurfaceTests.Generated_contract_counts_match_spec` still asserts 16 services / 31 unary RPCs (contract F3, FR-012, SC-005).

**Checkpoint**: The seam exists and the contract inventory names the RPC — user stories can now proceed in parallel

---

## Phase 3: User Story 1 - Read a large deal history without aborting the server (Priority: P1) 🎯 MVP

**Goal**: A .NET consumer can read a multi-thousand-deal history as a sequence of bounded
chunks via `await foreach`, with the server staying up. This is the only story that removes
the production crash loop and is the minimum viable slice.

**Independent Test**: Against the fake channel from T003, enumerate `StreamDealsAsync` over
a scripted multi-chunk deal sequence, concatenate the chunks, and assert the deals, their
order and their tickets match the expected sequence exactly with the call completing
normally — then repeat for empty, error-only, cancelled and abandoned reads.

### Tests for User Story 1 ⚠️

> **Write these first and confirm they FAIL (or do not compile) before T014.**

- [ ] T005 [P] [US1] Generated-surface contract test in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/StreamDealsTests.cs`: assert `Metatrader.V1.TradeHistoryService.TradeHistoryServiceClient` exposes `StreamDeals`, and that `Metatrader.V1.DealsRequest` exposes `ChunkSize`, `HasChunkSize` and `ClearChunkSize` with `DealsRequest.ChunkSizeFieldNumber == 5` (contract F1, F2; FR-012).
- [ ] T006 [P] [US1] Public signature test in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/StreamDealsTests.cs`: `Mt5GrpcClient.StreamDealsAsync` returns `IAsyncEnumerable<DealsResponse>` and takes `(DealsRequest?, DateTime?, CancellationToken)` with all three parameters optional (contract section B; FR-001).
- [ ] T007 [US1] Delivery tests in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/StreamDealsTests.cs`: (a) 1571 deals scripted as 4 chunks yields exactly 4 items whose concatenation holds all 1571 deals in server order with matching tickets and completes normally; (b) an empty filtered history yields zero items, raises nothing, and completes normally (US1-AC1, US1-AC2; FR-001, FR-003; contract B1).
- [ ] T008 [US1] Failure tests in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/StreamDealsTests.cs`: (a) an error-only `DealsResponse` (`error.code != 0`) terminates enumeration with `Mt5GrpcClientException` whose `.Error.Mt5ErrorCode` and `.Error.Message` carry the mapped values and yields no item from that message; (b) an `RpcException(StatusCode.Unimplemented)` — a pre-`0.4.0` server — terminates with the mapped error and the fake records **no** `GetDeals` invocation, proving there is no silent fallback (US1-AC3; FR-002, FR-006; contract B3, B4).
- [ ] T009 [US1] Lifetime tests in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/StreamDealsTests.cs`, each asserting the `WasDisposed` probe is set: (a) cancelling the supplied token mid-stream ends enumeration within 1 second with the mapped cancellation error; (b) an elapsed deadline ends enumeration with the mapped deadline error; (c) `break`-ing out of `await foreach` after the first chunk releases the call (US1-AC4, US1-AC5; FR-008, SC-004; contract B5, B6).
- [ ] T010 [US1] Chunk-size transmission tests in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/StreamDealsTests.cs`, reading the captured request: (a) caller sets nothing → `HasChunkSize == false`; (b) caller sets 250 → `ChunkSize == 250`; (c) caller sets 5000 (above the server cap) → `ChunkSize == 5000` is transmitted verbatim while the fake applies the documented clamp to 1000, and the client returns every deal without pre-empting, altering or re-reporting the value (FR-005, FR-013; contract B2).
- [ ] T011 [US1] Concurrency test in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/StreamDealsTests.cs`: two simultaneous `StreamDealsAsync` enumerations on one client are independent — cancelling or faulting one leaves the other yielding its remaining chunks in order (edge case "multiple concurrent streams"; contract B8).
- [ ] T012 [US1] Parity test matrix in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/StreamDealsTests.cs`: for each of the three filter forms (`time_filter`, `ticket`, `position`) with `group` set and unset (6 cases), the concatenation of streamed chunks equals `GetDealsAsync` for the same filters in deal count, order and ticket sequence, served from the same underlying list in the fake (US1-AC6; FR-007, SC-002; contract D).
- [ ] T013 [US1] Bounded-logging test in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/StreamDealsTests.cs` using an `ILoggerFactory` capture passed through `Mt5GrpcClientOptions.LoggerFactory`: a successful multi-chunk read emits no per-chunk and no per-deal log entry (output does not grow with history size), a failing read emits the existing `Mt5ErrorPayload`/`CallFailure` entries once, and no entry contains request payload, credentials or account identifiers (FR-010; contract B9). Note `TestLoggerProvider` lives in the `Tests` project and is not visible here — add a minimal local capture rather than a project reference.

### Implementation for User Story 1

- [ ] T014 [US1] Implement `StreamDealsAsync` in `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/Mt5GrpcClient.Trading.cs`, placed directly after `GetDealsAsync`: return `streamingInvoker.InvokeAsync("TradeHistoryService.StreamDeals", options => TradeHistory.StreamDeals(request ?? new DealsRequest(), options), response => response.Error, deadline, cancellationToken)`. Forward the caller's request instance unmodified — never set, raise, lower, clamp, default or clear `ChunkSize` or `Group` (FR-005). Add XML documentation stating: one item per server message, `await foreach` usage, the server's 500 default and 1000 cap with the warning that a chunk size at the cap (~77 KB) re-enters the observed abort band, that a pre-`0.4.0` server fails as unimplemented with no fallback, and that failures surface as `Mt5GrpcClientException`.
- [ ] T015 [US1] Run `dotnet test mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/MetaTrader.Grpc.Client.ContractTests.csproj -c Release --filter "FullyQualifiedName~StreamDeals"` and confirm all of T005–T013 pass with **no change** to `Mt5GrpcStreamingInvoker.cs`, `Mt5GrpcErrorMapper.cs`, `Mt5GrpcClientLogging.cs`, `Mt5GrpcClient.cs` or either factory. Per [research.md](./research.md) Decision 4 the existing invoker already satisfies FR-002/FR-003/FR-008/FR-010; if a production change outside `Mt5GrpcClient.Trading.cs` appears necessary, stop and record why before making it — that is a signal the design drifted.

**Checkpoint**: User Story 1 is fully functional and independently testable. The crash loop is escapable — this is a shippable MVP.

---

## Phase 4: User Story 2 - Reach the fix from .NET Framework 4.8 on the native channel (Priority: P2)

**Goal**: The streaming surface works for the majority population — .NET Framework 4.8 /
Windows 10 consumers on the native `Grpc.Core` channel via `Mt5GrpcClientFactory.CreateCore`
— not only for modern hosts.

**Independent Test**: A `net48` project calling `StreamDealsAsync` over a `CreateCore`
channel compiles and runs; `CompatibilityTests` confirms both signatures are reachable from
a `netstandard2.0`-consuming host with no additional package reference.

### Tests for User Story 2 ⚠️

- [ ] T016 [P] [US2] Reachability tests in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.CompatibilityTests/NetFramework48ReferenceTests.cs`, following the existing `NetFramework48_consumers_can_reference_every_trade_lifecycle_type_and_signature` pattern: assert `StreamDealsAsync` returns `IAsyncEnumerable<DealsResponse>` and `GetAllDealsAsync` returns `Task<Mt5GrpcResult<DealsResponse>>`, both with parameters `(DealsRequest, DateTime?, CancellationToken)` all optional; assert `DealsRequest.ChunkSize` is settable from this host with no extra package reference (contract E1, E3; US2-AC2, FR-009).
- [ ] T017 [P] [US2] Example-source assertion in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/StreamDealsTests.cs`, extending `NetFramework48_example_targets_net48_and_uses_grpc_core_channel`: the `net48` example's `Program.cs` contains both `Mt5GrpcClientFactory.CreateCore` and `StreamDealsAsync` (US2-AC1).

### Implementation for User Story 2

- [ ] T018 [US2] Add a minimal `StreamDealsAsync` backfill read to `mt5_grpc_client_csharp/examples/NetFramework48ClientExample/Program.cs` inside the existing `using (var client = Mt5GrpcClientFactory.CreateCore(options))` block: enumerate with `await foreach`, accumulate a deal count and the newest `TimeMsc`, and print them. Keep it small — the full documented pattern and its guidance comments land in T027. This call is the compile-time proof that the `net472` package asset exposes the surface and that `IAsyncEnumerable` resolves on `net48` from the already-shipped `Microsoft.Bcl.AsyncInterfaces`.
- [ ] T019 [US2] Run `dotnet build mt5_grpc_client_csharp/examples/NetFramework48ClientExample/NetFramework48ClientExample.csproj -c Release`, `dotnet test mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.CompatibilityTests/MetaTrader.Grpc.Client.CompatibilityTests.csproj -c Release`, and `mt5_grpc_client_csharp/scripts/verify-consumer-restore.ps1 -Configuration Release`. The `net48` build succeeding is the E1/E2 evidence; record it. US2-AC3 (prompt cancellation over the native channel) is covered behaviourally by T009 and confirmed on real transport in T041.

**Checkpoint**: User Stories 1 and 2 both work independently — the fix now reaches the population that hit the crash.

---

## Phase 5: User Story 3 - Get the whole history in one call when memory allows (Priority: P3)

**Goal**: A caller who needs the entire history materialised — a reconciliation job, a
report — gets a source-compatible drop-in for `GetDealsAsync` that is safe for the server.

**Independent Test**: Against the same fake channel, call `GetAllDealsAsync` and assert a
successful result whose deals equal the ordered concatenation of every chunk; then script an
error-only message and assert a failure result carrying the mapped error with no partial
collection and no exception.

### Tests for User Story 3 ⚠️

- [ ] T020 [P] [US3] Public signature test in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/StreamDealsTests.cs`: `Mt5GrpcClient.GetAllDealsAsync` returns `Task<Mt5GrpcResult<DealsResponse>>` — the same type as `GetDealsAsync` — with `(DealsRequest?, DateTime?, CancellationToken)` all optional (contract section C; FR-004).
- [ ] T021 [US3] Success tests in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/StreamDealsTests.cs`: (a) a several-chunk history returns `IsSuccess` with `Value.Deals` the ordered concatenation of every chunk, count equal to the chunk-count sum, tickets matching `GetDealsAsync` for the same filters, and `Value.Error == null`; (b) an empty filtered history returns `IsSuccess` with `Value.Deals.Count == 0` and `Error == null` (US3-AC1, US3-AC2; FR-003, FR-004; contract C1, C2).
- [ ] T022 [US3] Failure tests in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/StreamDealsTests.cs`, for each of an in-band error message, a transport `RpcException`, and a cancelled token: the method **returns** `IsSuccess == false` with `Error` carrying the mapped error and `Value == null` — it does not throw, and it returns no partial deal collection even when chunks were received before the fault (US3-AC3; FR-004; contract C3).

### Implementation for User Story 3

- [ ] T023 [US3] Implement `GetAllDealsAsync` in `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/Mt5GrpcClient.Trading.cs` after `StreamDealsAsync`: `await foreach` over `StreamDealsAsync(request, deadline, cancellationToken)`, appending each chunk's `Deals` into one freshly-constructed `DealsResponse`, and return `Mt5GrpcResult<DealsResponse>.Success(accumulated)`. Wrap the enumeration in the same catch ladder `Mt5GrpcUnaryInvoker` uses so nothing escapes: `Mt5GrpcClientException` (use `exception.Error` when non-null, else `Mt5GrpcErrorMapper.FromException`), `OperationCanceledException` (`FromCancellation`), and `Exception` (`FromException`) — each returning `Mt5GrpcResult<DealsResponse>.Failure(error)` and discarding accumulated deals. Do not double-log: the streaming invoker has already logged the failure.
- [ ] T024 [US3] Add XML documentation to `GetAllDealsAsync` in `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/Mt5GrpcClient.Trading.cs` stating that it retains the whole history in client memory, that `StreamDealsAsync` is the recommended surface for large histories, that failures are reported through the result rather than thrown, and that no partial collection is returned on failure (US3-AC4; FR-011; contract C5).

**Checkpoint**: All three capability stories are independently functional. Existing `GetDealsAsync` callers can migrate with a one-token rename.

---

## Phase 6: User Story 4 - Adopt the incremental polling pattern (Priority: P4)

**Goal**: Consumers stop re-reading the unchanging closed history every cycle — the habit
that both triggers the crash and wastes bandwidth — because the documentation and both
runnable examples show the intended pattern.

**Independent Test**: Read the README section and run both example projects; confirm each
demonstrates a backfill pass followed by an incremental fetch anchored on the last held
deal, and that a cycle with nothing new transfers no deals.

### Tests for User Story 4 ⚠️

- [ ] T025 [US4] Documentation-accuracy assertions in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/DocumentationAccuracyTests.cs`, following the existing `Readme_documents_every_lifecycle_category_and_safety_rule` and `Runnable_examples_cover_all_lifecycle_operations_and_result_inspection` patterns: assert the client README names `StreamDealsAsync`, `GetAllDealsAsync`, the chunk-size default `500` and cap `1000`, the incremental/anchor guidance, the unbounded-memory note, and the large-history failure mode of `GetDealsAsync`; and assert **both** example `Program.cs` files contain `StreamDealsAsync` and a `date_from`-anchored incremental fetch (`DateFrom`) (documentation contract H1–H5; FR-011, US4-AC1, US4-AC3).

### Implementation for User Story 4

- [ ] T026 [P] [US4] Extend `mt5_grpc_client_csharp/examples/NetStandardClientExample/Program.cs` with a deal-history section: backfill once through `StreamDealsAsync` (accumulating count and the newest `TimeMsc`), then perform an incremental fetch whose `TimeFilter.DateFrom` is that anchor, printing how many new deals arrived; add comments naming the chunk-size default and cap, the risk of requesting a size near the cap, and when to prefer `GetAllDealsAsync`.
- [ ] T027 [P] [US4] Extend the `StreamDealsAsync` read added in T018 in `mt5_grpc_client_csharp/examples/NetFramework48ClientExample/Program.cs` to the same backfill-then-incremental pattern with the same guidance comments, over the `CreateCore` native channel.
- [ ] T028 [US4] Add a "Deal history" section to `mt5_grpc_client_csharp/README.md` (placed next to the existing "Trade transaction events" section) covering all five points of documentation contract H: the backfill-then-incremental pattern with runnable snippets for both surfaces, the unbounded-memory cost of `GetAllDealsAsync`, the server's 500 default and 1000 cap plus the ~77 KB near-cap warning, the large-history failure mode of `GetDealsAsync` with a pointer to `StreamDealsAsync`, and that a pre-`0.4.0` server fails as unimplemented with no automatic fallback.
- [ ] T029 [US4] Add a note to the `GetDealsAsync` XML documentation in `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/Mt5GrpcClient.Trading.cs` describing the large-history failure mode and pointing to `StreamDealsAsync`. **Documentation only — the signature and behaviour must not change** (FR-006, contract section A; US4-AC3).

**Checkpoint**: All four user stories complete. Consumers have both a safe path and the guidance to use it well.

---

## Phase 7: Release, Gates & Cross-Cutting Concerns

**Purpose**: The coordinated `0.4.0`/`5.1.0` release, the shipped guards, and end-to-end validation

- [ ] T030 [P] Run `python bump_version.py minor` from the repository root and confirm both `mt5_grpc_server/setup.py` and `mt5_grpc_proto/setup.py` now read `version="0.4.0"` (FR-015, FR-016).
- [ ] T031 [P] Add a `## [0.4.0]` entry to the root `CHANGELOG.md` recording `TradeHistoryService.StreamDeals` and `DealsRequest.chunk_size` (field 5) as the additive contract change distinguishing `0.4.0` from `0.3.0`, noting no existing RPC, message, field or field number changed (FR-016).
- [ ] T032 [P] Add `mt5_grpc_server/tests/test_deals_history_stream.py` covering `TradeHistoryServiceImpl.StreamDeals` against a mock `MetaTrader5` module (follow `mt5_grpc_server/tests/test_trade_events_stream.py` for the mocking pattern): chunk boundaries for a history that is an exact and a non-exact multiple of the chunk size; `_chunk_size_for` clamping (unset → 500, 0 → 500, 5000 → 1000, 250 → 250); an empty history yielding zero messages; an MT5 failure yielding exactly one error-only message; and early exit when `context.is_active()` turns false. **Test-only — change no server behaviour.** This is the scope widening recorded in [research.md](./research.md) Decision 7: the RPC shipped in 95e3317 with no test, and `0.4.0` is what advertises it. Separable — if it is cut, say so in the release notes.
- [ ] T033 Update `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/MetaTrader.Grpc.Client.csproj`: `<Version>5.1.0</Version>`, `<ProtoContractIdentity>protos-007-stream-deals</ProtoContractIdentity>`, `<TestedServerVersionRange>[0.4.0,1.0.0)</TestedServerVersionRange>`, and rewrite `<PackageReleaseNotes>` for `5.1.0` quoting **both** new values verbatim. Add no package reference and change no existing one, so `check-package-metadata.ps1`'s exact per-target dependency-set assertion stays green (FR-014; contract G1, G2).
- [ ] T034 Update the compatibility header at the top of `mt5_grpc_client_csharp/README.md` (currently `5.0.0` / `protos-005-trade-transaction-events` / `[0.3.0,1.0.0)`) to `5.1.0` / `protos-007-stream-deals` / `[0.4.0,1.0.0)`, and add the additive-release callout for `StreamDeals`. **Must land together with T033** — `DocumentationAccuracyTests.Readme_and_release_notes_carry_current_compatibility_metadata` and `check-package-metadata.ps1` both fail while the csproj and README disagree (SC-010; contract G1).
- [ ] T035 [P] Add a `5.1.0` entry to `mt5_grpc_client_csharp/CHANGELOG.md`: the two new surfaces, `GetDealsAsync` preserved unchanged with no fallback in either direction, no new dependency and no target-framework change, and the moves to `protos-007-stream-deals` / `[0.4.0,1.0.0)` (FR-014).
- [ ] T036 [P] Add a section to `mt5_grpc_client_csharp/MIGRATION.md` for migrating off `GetDealsAsync`: `GetAllDealsAsync` is a one-token rename preserving `IsSuccess`/`Error`/`Value.Deals`, `StreamDealsAsync` is the preferred path for large histories, the memory trade-off between them, and the `0.4.0` server floor (FR-014).
- [ ] T037 [P] Grep the client package's documentation and metadata (`mt5_grpc_client_csharp/README.md`, `CHANGELOG.md`, `MIGRATION.md`, the csproj) for any statement that a `0.3.0` server supports the streaming surface, and remove or correct each one (SC-009; contract G3).
- [ ] T038 Run the full gate set and confirm every check passes: `dotnet build mt5_grpc_client_csharp/MetaTrader.Grpc.Client.sln -c Release`, `dotnet test mt5_grpc_client_csharp/MetaTrader.Grpc.Client.sln -c Release --no-build`, `mt5_grpc_client_csharp/scripts/check-generated.ps1 -Configuration Release`, `mt5_grpc_client_csharp/scripts/check-package-metadata.ps1 -Configuration Release`, `mt5_grpc_client_csharp/scripts/verify-consumer-restore.ps1 -Configuration Release`, and `python -m pytest mt5_grpc_server/tests -q` (quickstart scenario 1).
- [ ] T039 Confirm SC-005 explicitly: no pre-existing test, example or benchmark was modified to accommodate this change — `GeneratedSurfaceTests` still asserts 16 unary services / 31 unary RPCs, `UnaryWorkflowBenchmarks` and `PerformanceBudgetTests` are untouched, and the only edits to existing production files are the two added methods and the `GetDealsAsync` doc note in `Mt5GrpcClient.Trading.cs` plus the csproj metadata.
- [ ] T040 Reconcile the publish trigger before tagging: `mt5_grpc_client_csharp/README.md` documents a `csharp-client-v<X.Y.Z>` tag but `.github/workflows/csharp-client-publish.yml` triggers on `v*.*.*` (shared with the server's Docker release). Pick one and make the workflow and README agree, or `5.1.0` will not publish as documented. See [research.md](./research.md) Observation 1.
- [ ] T041 Run quickstart scenario 5 against a live `0.4.0`+ server and an account with 1600+ closed deals, and record the results in the PR: 20 consecutive `StreamDealsAsync` reads with zero server terminations and zero container restarts (SC-001); a `GetAllDealsAsync` vs `GetDealsAsync` spot check on a small account (SC-002); the same 20-read pass from a Windows 10 / .NET Framework 4.8 host over `CreateCore` matching a `Create` read (SC-006, US2-AC1, US2-AC3); working-set observation showing chunk-level retention tracks one chunk (SC-007); and a steady-state incremental cycle transferring zero deals with no new activity and exactly N with N new deals (SC-008). **If any part cannot be run, state which and why** — the constitution requires unrun checks to be documented.
- [ ] T042 Publish gate (do not skip): confirm that a `0.4.0` release of `mt5_grpc_proto/setup.py` and `mt5_grpc_server/setup.py` satisfying `[0.4.0,1.0.0)` exists **before** publishing the client `5.1.0`. All client verification above may complete beforehand; only the publish step is blocked (FR-016; contract G4).
- [ ] T043 [P] *Optional, out of this feature's scope — decide explicitly rather than silently:* replace the hard-coded login, password, broker address and terminal path constants in `mt5_grpc_client_csharp/examples/NetFramework48ClientExample/Program.cs` with argument-or-environment lookups. Committed credentials sit in code this feature edits; folding the fix in quietly would hide it, and leaving it unmentioned would too. See [research.md](./research.md) Observation 2.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately. T002 must precede any task that references the generated `StreamDeals` stub.
- **Foundational (Phase 2)**: Depends on Phase 1. **Blocks every user story** — T003 is the seam all behaviour tests are written against.
- **User Story 1 (Phase 3)**: Depends on Phase 2. No dependency on any other story.
- **User Story 2 (Phase 4)**: Depends on Phase 2; T016 references `GetAllDealsAsync` (T023), so either run Phase 4 after Phase 5 or split T016 so the `StreamDealsAsync` assertions land first. T018 needs T014.
- **User Story 3 (Phase 5)**: Depends on Phase 2, and T023 consumes `StreamDealsAsync` (T014) — the convenience surface is a wrapper, so US3 genuinely follows US1 rather than merely being lower priority.
- **User Story 4 (Phase 6)**: Depends on T014 (both examples call `StreamDealsAsync`); T026/T027 read best after T023 so the memory trade-off can be shown concretely.
- **Release & Polish (Phase 7)**: T033/T034 must land together. T038 depends on every code and doc task. T041 depends on a deployed `0.4.0` server. T042 gates only the publish.

### User Story Dependencies

- **US1 (P1)**: Independent. Deliverable alone as the MVP.
- **US2 (P2)**: Depends on US1's method existing; independently testable once it does.
- **US3 (P3)**: Wraps US1's surface. Independently testable via its own result-shape assertions.
- **US4 (P4)**: Documentation and examples over US1 (+US3 for the trade-off narrative). Independently verifiable by reading the README and running both examples.

### Within Each User Story

- Tests are written first and must fail (or fail to compile) before the implementation task.
- Contract/signature tests before behaviour tests; behaviour tests before implementation.
- Implementation before the story's verification/run task.
- Finish a story to its checkpoint before starting the next priority.

### Parallel Opportunities

- T004 runs parallel to T003.
- T005, T006 in parallel (independent assertions, same new file — coordinate the single file creation or write T005 first).
- T016, T017 in parallel (US2 tests, same file — same caveat).
- T026, T027 in parallel (different example projects).
- T030, T031, T032 in parallel (Python release work, three different files).
- T035, T036, T037, T043 in parallel (different documentation files).
- With multiple developers: after Phase 2, one developer takes US1 → US3 (the code path) while another takes US4's README and the Phase 7 release work; US2 joins once T014 lands.

---

## Parallel Example: Phase 7 release work

```bash
# Python release trio (three separate files, no interdependency):
Task: "Run python bump_version.py minor and verify both setup.py files read 0.4.0"
Task: "Add [0.4.0] entry to root CHANGELOG.md naming StreamDeals and chunk_size"
Task: "Add mt5_grpc_server/tests/test_deals_history_stream.py with mock MT5 coverage"

# Client documentation trio (three separate files):
Task: "Add 5.1.0 entry to mt5_grpc_client_csharp/CHANGELOG.md"
Task: "Add GetDealsAsync migration section to mt5_grpc_client_csharp/MIGRATION.md"
Task: "Grep documentation and metadata for stale 0.3.0 support claims"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1: Setup (T001–T002).
2. Phase 2: Foundational (T003–T004) — **critical, blocks everything**.
3. Phase 3: User Story 1 (T005–T015).
4. **STOP and VALIDATE**: quickstart scenario 2 green; a consumer can read a large history
   safely with `await foreach`.
5. Optionally cut a `5.1.0-preview` here — the crash loop is escapable with US1 alone.

### Incremental Delivery

1. Setup + Foundational → seam and inventory ready.
2. + US1 → **MVP**: the crash is avoidable (validate, demo).
3. + US2 → the fix reaches .NET Framework 4.8 / native-channel consumers, the population
   that actually hit the crash (validate, demo).
4. + US3 → one-call convenience for reconciliation callers (validate, demo).
5. + US4 → documentation and examples teach the incremental pattern.
6. + Phase 7 → coordinated `0.4.0`/`5.1.0` release, gates green, publish once T042 clears.

Each increment is additive; none breaks a previous one, and `GetDealsAsync` behaves
identically throughout.

### Parallel Team Strategy

1. Everyone on Setup + Foundational.
2. Developer A: US1 (T005–T015), then US3 (T020–T024) — the code path.
3. Developer B: US4 README (T028) and Phase 7 release work (T030–T037) from the contract
   documents, no code dependency.
4. Developer C: joins on US2 (T016–T019) once T014 lands, then the examples (T026–T027).
5. Converge on T038–T042.

---

## Notes

- **Expected production diff**: two methods plus a documentation note in
  `Mt5GrpcClient.Trading.cs`, and the csproj metadata. Nothing else in
  `src/MetaTrader.Grpc.Client/` should change (T015 guards this).
- **No proto change, no generated-code edit, no server behaviour change** — the contract
  and server implementation already carry `StreamDeals` and `chunk_size`.
- **No new NuGet dependency**: `Microsoft.Bcl.AsyncInterfaces` already ships, and
  `check-package-metadata.ps1` asserts an exact per-target dependency set.
- [P] tasks touch different files with no incomplete dependency. Where two [P] tasks share
  a new file, create the file in the first and add to it in the second.
- Verify each test fails before implementing against it.
- Commit after each task or logical group; stop at any checkpoint to validate a story
  independently.
