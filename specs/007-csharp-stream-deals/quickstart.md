# Quickstart & Validation Guide: C# Client StreamDeals Surface

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Contract**: [contracts/client-surface.md](./contracts/client-surface.md)

How to run and validate this feature end to end. Scenarios 1–4 need nothing but the
repository and the .NET 9 SDK; scenario 5 needs a live MT5 account with a large history and
is the only one that reproduces the original crash. Implementation belongs in `tasks.md` —
this document says how to *prove* the work, not how to write it.

## Prerequisites

| Scenario | Needs |
| --- | --- |
| 1–4 (automated) | .NET SDK 9.0.x, Windows or Linux, PowerShell 7 for the `scripts/*.ps1` gates. No broker, no MT5 terminal, no Wine. |
| Release checks | Python 3.8+ (for `bump_version.py`). |
| 5 (manual, SC-001/SC-006/SC-008) | A running `mt5_grpc_server` at `0.4.0` or later, an MT5 account with 1600+ closed deals, and for SC-006 a Windows 10 / .NET Framework 4.8 host. |

---

## Scenario 1 — Build, test and the shipped gates (all requirements except SC-001/006/008)

```powershell
dotnet restore mt5_grpc_client_csharp/MetaTrader.Grpc.Client.sln
dotnet build   mt5_grpc_client_csharp/MetaTrader.Grpc.Client.sln -c Release --no-restore
dotnet test    mt5_grpc_client_csharp/MetaTrader.Grpc.Client.sln -c Release --no-build
mt5_grpc_client_csharp/scripts/check-generated.ps1         -Configuration Release
mt5_grpc_client_csharp/scripts/check-package-metadata.ps1  -Configuration Release
mt5_grpc_client_csharp/scripts/verify-consumer-restore.ps1 -Configuration Release
```

Expected:

- **Build** succeeds for both library targets and for all seven solution projects,
  including `NetFramework48ClientExample` (`net48`). That build *is* the reachability proof
  for contract E1/E2 — a `net48` project calling `StreamDealsAsync` and `GetAllDealsAsync`
  through `Mt5GrpcClientFactory.CreateCore` cannot compile unless the `net472` asset
  exposes both, with `IAsyncEnumerable` resolving from the already-shipped
  `Microsoft.Bcl.AsyncInterfaces`.
- **Test** passes with no test modified to accommodate the change. `GeneratedSurfaceTests`
  still asserts 16 unary services / 31 unary RPCs (F3), and every pre-existing test,
  example and benchmark compiles untouched (SC-005).
- **check-generated** confirms the C# bindings rebuild from `protos/*.proto` — the
  `StreamDeals` stub and `DealsRequest.ChunkSize` come from generation, not from authored
  code.
- **check-package-metadata** confirms `protos-007-stream-deals` and `[0.4.0,1.0.0)` appear
  in the packed README and release notes (G1) and that the per-target dependency set is
  unchanged (G2). This gate fails loudly if the csproj metadata is bumped without updating
  the README header, which currently still reads `5.0.0` / `protos-005-trade-transaction-events`
  / `[0.3.0,1.0.0)`.

## Scenario 2 — Streaming behaviour against the fake channel (US1, US3)

```powershell
dotnet test mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/MetaTrader.Grpc.Client.ContractTests.csproj -c Release --filter "FullyQualifiedName~StreamDeals"
```

Every case runs in-process with no socket, driving the real public methods and the real
`Mt5GrpcStreamingInvoker` through the `ChannelBase` / `CallInvoker` doubles described in
[research.md](./research.md) Decision 3.

| Case | Expected outcome | Requirement |
| --- | --- | --- |
| 1571 deals served as 4 chunks | 4 items yielded; concatenation has 1571 deals in server order with matching tickets; normal completion | US1-AC1, B1 |
| Empty filtered history | 0 items, no error, normal completion | US1-AC2, FR-003 |
| Error-only message | `Mt5GrpcClientException` with the mapped `Mt5ErrorCode` and message; no item yielded | US1-AC3, B3 |
| Transport `Unimplemented` | mapped error raised; **no** call to `GetDeals` observed on the fake | B4, FR-006 |
| Cancel mid-stream | enumeration ends within 1 s; dispose probe shows the call released | US1-AC4, B5, SC-004 |
| Deadline elapses mid-stream | mapped deadline error; call released | US1-AC5, B5 |
| `break` after the first chunk | dispose probe shows the call released | B6, FR-008 |
| Chunk size unset | captured request has `HasChunkSize == false` | B2, FR-005 |
| Chunk size 250 | captured request has `ChunkSize == 250` | B2 |
| Chunk size 5000 (above cap) | captured request has `ChunkSize == 5000`; the fake applies the server's documented clamp to 1000 and the client returns every deal without pre-empting or reporting a different value | B2, FR-013 |
| Two concurrent streams | independent; cancelling one leaves the other yielding | B8 |
| `GetAllDealsAsync` over several chunks | success result; `Value.Deals` is the ordered concatenation | US3-AC1, C1 |
| `GetAllDealsAsync` over an empty history | success result, 0 deals, no error | US3-AC2, C2 |
| `GetAllDealsAsync` with a mid-stream fault | failure result carrying the mapped error, `Value == null`, **no throw** | US3-AC3, C3 |
| Parity matrix: 3 filter forms × `group` set/unset | `GetAllDealsAsync` and concatenated chunks equal `GetDealsAsync` in count, order and tickets | FR-007, SC-002, contract D |

## Scenario 3 — .NET Framework reachability (US2)

```powershell
dotnet test  mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.CompatibilityTests/MetaTrader.Grpc.Client.CompatibilityTests.csproj -c Release
dotnet build mt5_grpc_client_csharp/examples/NetFramework48ClientExample/NetFramework48ClientExample.csproj -c Release
```

Expected: both signatures are reachable by reflection from a `netstandard2.0`-consuming
host with the documented parameter types and optionality; the `net48` example builds and
its source is asserted to use `Mt5GrpcClientFactory.CreateCore` together with
`StreamDealsAsync` (E1–E3, US2-AC2). US2-AC1 and US2-AC3 — identical results and prompt
cancellation over the native channel — are covered against the fake channel in scenario 2
for behaviour and confirmed on real transport in scenario 5.

## Scenario 4 — Documentation and examples (US4)

```powershell
dotnet test mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/MetaTrader.Grpc.Client.ContractTests.csproj -c Release --filter "FullyQualifiedName~DocumentationAccuracy"
dotnet run --project mt5_grpc_client_csharp/examples/NetStandardClientExample/NetStandardClientExample.csproj   # needs a reachable server
```

Expected: the README section and both examples carry all five points of documentation
contract H, and each example demonstrates a backfill pass through `StreamDealsAsync`
followed by an incremental fetch anchored on the last held deal (US4-AC1..AC3).

> Before editing the examples, resolve the uncommitted working-tree changes in both
> `Program.cs` files ([research.md](./research.md), Observation 3) so the FR-011 edits do
> not land on top of debugging scratch.

## Scenario 5 — Manual verification against a live account (SC-001, SC-006, SC-007, SC-008)

Requires a server at `0.4.0`+ and an account with 1600+ closed deals.

1. **Baseline (optional, destructive)** — call `GetDealsAsync` for the full history and
   observe the server abort (`windows_endpoint.cc:363`, exit `c0000409`) and the
   `restart: unless-stopped` loop. This is the behaviour the feature removes; skip it if the
   crash loop is unacceptable in the environment.
2. **SC-001** — read the same history through `StreamDealsAsync` 20 consecutive times.
   Expected: 20 successful reads, zero server terminations, zero container restarts
   (`docker ps` uptime unbroken, no new `Restarting` transitions).
3. **SC-002 spot check** — for a smaller account where both calls are safe, compare
   `GetDealsAsync` and `GetAllDealsAsync`: identical count, order and tickets.
4. **SC-006** — repeat step 2 from a Windows 10 / .NET Framework 4.8 host using
   `Mt5GrpcClientFactory.CreateCore`, and confirm the concatenated result matches a read
   over `Mt5GrpcClientFactory.Create`.
5. **SC-007** — watch process working set during a chunk-level read: it tracks roughly one
   chunk, not the whole history. Contrast with `GetAllDealsAsync`, whose retention grows
   with the history (the documented trade-off).
6. **SC-008** — after one backfill, run an incremental cycle anchored on the newest held
   deal with no new activity: zero deals transferred. Generate N new deals and re-run:
   exactly N transferred.
7. **Chunk-size caution** — optionally request `ChunkSize = 1000` (the cap, ~77 KB) and
   note that this re-enters the observed uncertain band. Do this only on a disposable
   server; it is the risk the documentation warns about, not a supported configuration.

## Release validation (FR-014..FR-016, SC-009)

```powershell
python bump_version.py minor      # mt5_grpc_server/setup.py + mt5_grpc_proto/setup.py: 0.3.0 -> 0.4.0
python -m pytest mt5_grpc_server/tests -q
```

Then, in order:

1. Confirm both `setup.py` files read `0.4.0` and the root `CHANGELOG.md` has a `[0.4.0]`
   entry naming `StreamDeals` and `chunk_size` as the additive contract change.
2. Confirm the client csproj reads `<Version>5.1.0</Version>`,
   `<ProtoContractIdentity>protos-007-stream-deals</ProtoContractIdentity>`,
   `<TestedServerVersionRange>[0.4.0,1.0.0)</TestedServerVersionRange>`, and that both
   values appear verbatim in the client README and `<PackageReleaseNotes>`.
3. Grep the package documentation and metadata for any claim that a `0.3.0` server supports
   the streaming surface (G3) — there must be none.
4. **Publish gate (G4)**: do not publish the client `5.1.0` until a `0.4.0` `mt5_grpc_proto`
   and `mt5_grpc_server` release satisfying `[0.4.0,1.0.0)` exists. All client verification
   above can complete before that release; only the publish step is blocked.
5. Note before tagging: the client README documents a `csharp-client-v*` tag but
   `.github/workflows/csharp-client-publish.yml` triggers on `v*.*.*`
   ([research.md](./research.md), Observation 1). Reconcile the two, or the release will
   not fire the intended workflow.

## Definition of done

- [X] Scenarios 1–4 pass with no pre-existing test, example or benchmark modified to
      accommodate the change (SC-005). 250 C# tests + 48 pytest, all green; the diff to
      existing test files is additive only (`+55/-0`, `+56/-0`, and `+4/-1` in
      `ProtoContractCatalog` where a trailing comma was added).
- [X] `StreamDealsAsync` and `GetAllDealsAsync` satisfy every guarantee in
      [contracts/client-surface.md](./contracts/client-surface.md) sections B, C, D.
- [X] `GetDealsAsync` is byte-for-byte unchanged apart from XML documentation (section A).
      `git diff` on `Mt5GrpcClient.Trading.cs` shows zero removed lines.
- [X] Parity matrix green for all three filter forms with `group` set and unset — 6 cases
      for the streamed concatenation and 6 for `GetAllDealsAsync`, plus a guard test
      asserting the `group` filter actually narrows the result so the matrix is not vacuous.
- [X] `ProtoContractCatalog.StreamingServices` includes `TradeHistoryService`/`StreamDeals`
      and `UnaryServices` is unchanged (F3) — `GeneratedSurfaceTests` still asserts
      16 services / 31 unary RPCs and its file is untouched.
- [X] Version and compatibility metadata bumped, quoted verbatim, both guards green
      (G1, G2, SC-010). `check-package-metadata.ps1` reports
      `protos-007-stream-deals` and `[0.4.0,1.0.0)` in README + releaseNotes, and an
      unchanged per-target dependency set.
- [~] Python packages **set to** `0.4.0` with a root changelog entry, and the server RPC
      covered by `mt5_grpc_server/tests/test_deals_history_stream.py` (research Decision 7,
      23 tests). **Not yet released** — see the publish gate below.
- [ ] **Scenario 5 NOT RUN — documented in [tasks.md](./tasks.md).** Needs a live
      `0.4.0`+ server, an account with 1600+ closed deals, and a Windows 10 / .NET
      Framework 4.8 host, none of which were available. SC-001, SC-006, SC-007 and SC-008
      remain unverified against real transport; run it before release.
- [ ] **Publish gate (G4) CLOSED.** No `0.4.0` `mt5_grpc_proto` / `mt5_grpc_server`
      release exists yet, so the client `5.1.0` publish must not proceed. All client-side
      verification above is complete and need not be repeated.
