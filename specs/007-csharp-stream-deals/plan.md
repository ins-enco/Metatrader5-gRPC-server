# Implementation Plan: C# Client StreamDeals Surface

**Branch**: `009-csharp-stream-deals` | **Date**: 2026-09-07 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/007-csharp-stream-deals/spec.md`

## Summary

Expose the already-shipped `TradeHistoryService.StreamDeals` server-streaming RPC on the
C# client so a .NET consumer can read a large deal history without terminating the
server. Today `Mt5GrpcClient` offers only `GetDealsAsync`
(`mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/Mt5GrpcClient.Trading.cs:140`); a
~1600-deal history (~121 KB in one message) aborts grpcio's Windows IOCP endpoint and
puts the container into a restart loop.

Two additive public surfaces are added to the existing `Mt5GrpcClient.Trading.cs`
partial, both routed through the **existing** `Mt5GrpcStreamingInvoker` (which needs no
change):

- `StreamDealsAsync(...)` returning `IAsyncEnumerable<DealsResponse>` — chunk-level, 1:1
  with the server stream, consumed with `await foreach` (FR-001..FR-003, FR-008).
- `GetAllDealsAsync(...)` returning `Task<Mt5GrpcResult<DealsResponse>>` — consumes the
  stream internally and returns one synthesized `DealsResponse` whose `Deals` is the
  ordered concatenation, so it is a source-compatible drop-in for `GetDealsAsync` callers
  (FR-004).

`GetDealsAsync` is untouched, no proto changes are made, and no NuGet dependency is added
(`Microsoft.Bcl.AsyncInterfaces` already ships for the `netstandard2.0`
`IAsyncEnumerable` path). The generated `StreamDeals` stub and `DealsRequest.ChunkSize`
arrive from `Grpc.Tools` on the next build.

Beyond the client, the plan performs the coordinated release the spec requires: the Python
packages move `0.3.0` to `0.4.0` via `bump_version.py minor` so the RPC is gated by a
checkable version, the client moves `5.0.2` to `5.1.0`, and the authored compatibility
metadata moves to `protos-007-stream-deals` / `[0.4.0,1.0.0)` (FR-014..FR-016).

**Testing approach (Phase 0 Decision 3)**: the repository has *no* in-process gRPC server
— `GrpcTestServerFixture` is an address-only stub and `ContractTests` references
`Grpc.Core.Api` only. Streaming behaviour is therefore driven through a fake `ChannelBase`
plus `CallInvoker` that returns canned `AsyncServerStreamingCall` / `AsyncUnaryCall`
objects and captures the transmitted `DealsRequest`. That seam exercises the real public
methods and the real invoker, needs no socket, no live broker and no new package
reference, and is the only way to assert unary/streaming parity (FR-007) and verbatim
chunk-size transmission (FR-005) deterministically.

## Technical Context

**Language/Version**: C# (`LangVersion` latest) targeting `netstandard2.0` + `net472`;
test and example hosts on `net9.0` and `net48`. Python 3.8+ for the release-only package
bump.

**Primary Dependencies**: `Google.Protobuf` 3.29.3, `Grpc.Core.Api` 2.71.0,
`Grpc.Net.Client` 2.71.0, `Grpc.Tools` 2.71.0 (`PrivateAssets="all"`),
`Microsoft.Bcl.AsyncInterfaces` 9.0.0, `Microsoft.Extensions.Logging.Abstractions` 9.0.0;
`net472` only: `Grpc.Core` 2.46.6 (native channel). **No dependency is added or changed** —
`check-package-metadata.ps1` asserts an exact dependency set per target framework.

**Storage**: N/A. The read anchor for incremental polling is caller-held state
(documentation only).

**Testing**: xUnit 2.9.3 across `ContractTests` (generated surface, streaming behaviour,
parity, documentation drift), `Tests` (unit), `CompatibilityTests` (.NET Framework
reachability), plus compile-time proof from the `net48` and `net9.0` example projects,
which the solution build covers. New fake channel/invoker fixtures; no live broker, MT5
terminal or Wine environment.

**Target Platform**: .NET Framework 4.8 / Windows 10 over the native `Grpc.Core` channel
(`Mt5GrpcClientFactory.CreateCore`) is a first-class target, alongside modern .NET over
`GrpcChannel` (`Mt5GrpcClientFactory.Create`).

**Project Type**: Multi-language repo — canonical protos in `protos/`, generated Python in
`mt5_grpc_proto/`, Python server in `mt5_grpc_server/`, C# client library in
`mt5_grpc_client_csharp/`. This feature is client-library plus release work.

**Performance Goals**: Peak client retention for a chunk-level read scales with one chunk,
not the history (SC-007). Cancellation returns control within 1 s (SC-004). 20 consecutive
reads of a 1600+ deal history with zero server terminations (SC-001). A steady-state
incremental cycle with no new activity transfers zero deals (SC-008).

**Constraints**: The server owns the chunk-size default (500) and cap (1000) — the client
transmits the caller's value verbatim and supplies none of its own (FR-005). A chunk size
at the cap is ~77 KB, inside the observed 75 KB-safe / 121 KB-fatal band, so guidance must
warn against it. `GetDealsAsync` keeps its exact signature and behaviour, and neither
operation falls back to the other (FR-006). Logging stays bounded and credential-free
(FR-010). Authored compatibility values must appear verbatim in the packed README and
`PackageReleaseNotes` or the existing drift guard fails (FR-014, SC-010).

**Scale/Scope**: Two new public methods in one existing partial file; zero changes to
`Mt5GrpcStreamingInvoker`, `Mt5GrpcErrorMapper`, the factories or `Mt5GrpcClient.cs`; one
contract-catalog entry; two new test files plus additions to two existing ones;
README/CHANGELOG/MIGRATION sections; both runnable examples; csproj version and metadata;
`bump_version.py minor` and a root `CHANGELOG.md` entry.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Protocol contract**: **PASS**. `protos/deal.proto` already declares
  `StreamDeals(DealsRequest) returns (stream DealsResponse)` and
  `optional uint32 chunk_size = 5`; **this feature changes no `.proto` file**. No field
  number is added, reused, renamed or removed; `GetDeals`, `DealsResponse` and `Deal` are
  untouched. C# bindings are regenerated by `Grpc.Tools` from `protos/*.proto` at build
  time and never hand-edited; the `check-generated.ps1` gate proves they rebuild.
  Generated-surface coverage is asserted through `ProtoContractCatalog.StreamingServices`
  plus a contract test (FR-012). See [research.md](./research.md) Decisions 1 and 2 and
  [contracts/client-surface.md](./contracts/client-surface.md).

- **MT5 behavior fidelity**: **PASS**. Both RPCs read closed history through the same
  server helper `_fetch_deals`
  (`mt5_grpc_server/mt5_grpc_server/imp/deals_history.py`), which wraps
  `mt5.history_deals_get`. The client adds **no** validation, coercion, filtering,
  defaulting or reordering: the caller's `DealsRequest` is forwarded as-is, so filter
  semantics (time filter / order ticket / position ticket, optional `group`), deal
  tickets, order and position identifiers, `time` (Unix seconds) and `time_msc`
  (milliseconds), and MT5 error codes and messages are exactly those of `GetDeals`. An
  error-only message surfaces the mapped MT5 code through the existing
  `Mt5GrpcErrorMapper.FromMt5Error` path rather than being masked; a request with no
  filter still yields the server's `-2` "No valid filter criteria provided". Parity is
  test-enforced for all three filter forms with `group` set and unset (FR-007, SC-002).
  See [data-model.md](./data-model.md) and [research.md](./research.md) Decision 5.

- **Multi-language type safety**: **PASS**. No new contract surface is introduced, so no
  new cross-language ambiguity can be. The one presence-sensitive field, `chunk_size`, is
  proto3 `optional`: the client never sets it and never clears it, so "unset" stays unset
  on the wire and the server's own default applies — a transmitted `0` stays distinct from
  unset and is left to the server's clamping. The `filter` oneof keeps exactly one active
  form. Chunk order is the server's iteration order and within-chunk order is
  repeated-field order; their concatenation is the caller-visible total order. No sentinel
  values, no Python-only concepts and no C#-only shapes cross the boundary. See
  [data-model.md](./data-model.md) *Cross-Language Type Notes*.

- **Reliability and observability**: **PASS**. Failure modes are enumerated and mapped:
  an in-band error frame becomes `Mt5GrpcClientException` carrying the mapped
  `Mt5GrpcError` (chunk-level) or an `Mt5GrpcResult` failure (convenience); a transport
  fault becomes the same; an unimplemented method on a pre-`0.4.0` server becomes a mapped
  `Unimplemented` error with **no** silent fallback to `GetDeals`, since a fallback would
  restore the crash. Cancellation and deadline are honoured through
  `Mt5GrpcCallOptions.Create` and the invoker's `MoveNext(cancellationToken)`; the
  invoker's `using var streamingCall` releases the call on normal end, fault, cancellation
  **and early `break`**, which a test asserts through a dispose probe (FR-008, SC-004).
  Resource bounds: the chunk-level surface retains one chunk at a time (SC-007) and the
  convenience surface's whole-history retention is documented at the point of use as the
  explicit trade-off. Logging reuses the existing bounded `Mt5GrpcClientLogging` helpers —
  per call and per failure only, never per deal — and emits no request payload,
  credentials or account identifiers (FR-010). See [research.md](./research.md)
  Decisions 4 and 6.

- **Test and release reproducibility**: **PASS**. Every check is a repository command:
  `dotnet restore/build/test` on the solution, `check-generated.ps1`,
  `check-package-metadata.ps1`, `verify-consumer-restore.ps1`, and
  `python bump_version.py minor` for the coordinated `0.4.0` release — all listed with
  invocations in [quickstart.md](./quickstart.md). New behaviour is covered at the lowest
  practical level by a no-broker fake-channel seam (multi-chunk concatenation, empty
  history, error-only frame, cancellation mid-stream, early-abandon release, verbatim
  chunk-size transmission including an above-cap value, and unary/streaming parity), with
  .NET Framework reachability in `CompatibilityTests` plus compile-time proof from the
  `net48` example. SC-005 (no source change for existing callers) is proven by the
  existing suites, examples and benchmarks compiling and passing unmodified.

  **One recorded widening, not a violation**: the server's `StreamDeals` shipped in
  95e3317 with no `pytest`, so Principle V's "no live broker test path for new RPC
  behavior" is currently unmet for the RPC that `0.4.0` will publicly advertise. Because
  this plan performs that release, it adds one **test-only** file
  (`mt5_grpc_server/tests/test_deals_history_stream.py`, mock MT5, no behaviour change)
  covering chunking, clamping, empty history and error-only output. This is beyond the
  spec's FR list and is flagged as such in [research.md](./research.md) Decision 7. Drop it
  and the gate still passes on client coverage alone, but the `0.4.0` release then
  advertises an RPC with no automated test.

**Result**: All five gates PASS. No entries required in Complexity Tracking.

## Post-Design Constitution Re-Check

Re-evaluated after Phase 1 (`data-model.md`, `contracts/client-surface.md`,
`quickstart.md`) with no gate changing verdict:

- The design added **no** proto surface and **no** dependency, so the *protocol contract*
  and *multi-language type safety* gates are strictly narrower after design than before:
  the only presence-sensitive decision (`chunk_size` is forwarded, never authored by the
  client) is now written down as a contract rule with a test attached.
- *MT5 fidelity* is now enforced mechanically rather than by review: the parity contract
  in `contracts/client-surface.md` fixes count, order and ticket equality against
  `GetDealsAsync` across all three filter forms with `group` set and unset.
- *Reliability* gained one design commitment during Phase 1 — the convenience surface
  discards partial results on failure and never raises — so a caller cannot mistake a
  truncated history for a complete one.
- *Test and release reproducibility* is unchanged; the release ordering constraint (client
  `5.1.0` must not publish before a `0.4.0` Python release exists, FR-016) is captured as
  a gate in `quickstart.md` rather than left to reviewer memory.

## Project Structure

### Documentation (this feature)

```text
specs/007-csharp-stream-deals/
├── plan.md                      # This file (/speckit-plan output)
├── research.md                  # Phase 0 output
├── data-model.md                # Phase 1 output
├── quickstart.md                # Phase 1 output
├── contracts/                   # Phase 1 output
│   ├── client-surface.md        # Authored public C# surface contract (the deliverable)
│   └── deal.proto               # Snapshot of the UNCHANGED wire contract, for reference
├── checklists/
│   └── requirements.md          # Existing spec-quality checklist (16/16 pass)
└── tasks.md                     # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
protos/
└── deal.proto                             # UNCHANGED — StreamDeals + chunk_size already present

mt5_grpc_proto/setup.py                    # EDIT (release only): 0.3.0 -> 0.4.0 via bump_version.py
mt5_grpc_server/setup.py                   # EDIT (release only): 0.3.0 -> 0.4.0 via bump_version.py
mt5_grpc_server/mt5_grpc_server/imp/
└── deals_history.py                       # UNCHANGED — _fetch_deals / StreamDeals already correct
mt5_grpc_server/tests/
└── test_deals_history_stream.py           # NEW (test-only, mock MT5): chunking, clamp, empty, error
CHANGELOG.md                               # EDIT: [0.4.0] entry naming StreamDeals + chunk_size

mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/
├── Mt5GrpcClient.Trading.cs               # EDIT: + StreamDealsAsync, + GetAllDealsAsync (GetDealsAsync untouched)
└── MetaTrader.Grpc.Client.csproj          # EDIT: Version 5.1.0, ProtoContractIdentity,
                                           #       TestedServerVersionRange, PackageReleaseNotes
                                           # NO CHANGE: Mt5GrpcStreamingInvoker.cs, Mt5GrpcClient.cs,
                                           #            Mt5GrpcErrorMapper.cs, both factories

mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/
├── Fixtures/FakeStreamingChannel.cs       # NEW: ChannelBase + CallInvoker doubles, canned streams, request capture
├── StreamDealsTests.cs                    # NEW: surface, multi-chunk, empty, error-only, cancel,
│                                          #      release-on-abandon, chunk-size, unary parity
├── ProtoContractCatalog.cs                # EDIT: StreamingServices += TradeHistoryService/StreamDeals
│                                          #       (UnaryServices unchanged: 16 services / 31 RPCs)
└── DocumentationAccuracyTests.cs          # EDIT: README + both examples assert the new guidance

mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.CompatibilityTests/
└── NetFramework48ReferenceTests.cs        # EDIT: both signatures reachable; net48 example uses
                                           #       CreateCore + StreamDealsAsync

mt5_grpc_client_csharp/examples/
├── NetStandardClientExample/Program.cs    # EDIT: backfill via StreamDealsAsync, then incremental fetch on the anchor
└── NetFramework48ClientExample/Program.cs # EDIT: same pattern over the CreateCore native channel

mt5_grpc_client_csharp/
├── README.md                              # EDIT: new "Deal history" section + updated header metadata
├── CHANGELOG.md                           # EDIT: 5.1.0 entry
└── MIGRATION.md                           # EDIT: GetDealsAsync -> GetAllDealsAsync / StreamDealsAsync notes
```

**Structure Decision**: The feature slots into the library's established layout with no new
architectural element. Public operations live in `Mt5GrpcClient.*.cs` partials over the
shared `Mt5GrpcClient`, so both new methods go in the existing `Mt5GrpcClient.Trading.cs`
beside `GetDealsAsync` rather than in a new partial. Streaming already has its invoker
(`Mt5GrpcStreamingInvoker`, added for `SubscribeTradeTransactions`) and the
`TradeHistoryService` client (`TradeHistory`) is already constructed in
`Mt5GrpcClient.cs` — both are reused unchanged, which is why the production diff is two
methods plus a csproj edit. Test doubles go under the existing `ContractTests/Fixtures/`
directory next to `streaming_fixture.proto`, and contract inventory stays in
`ProtoContractCatalog` with the streaming/unary split that already exists to keep the
unary counts (16 services / 31 RPCs) stable.

## Complexity Tracking

> No Constitution Check violations. This section intentionally left empty.
