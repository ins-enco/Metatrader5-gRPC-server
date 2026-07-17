# Quickstart: Trade Lifecycle Operations

This is the implementation and verification path for the client-only feature.
Commands assume the repository root and PowerShell. Unit tests use scripted
delegates and require no live MT5 terminal or broker account.

## 0. Prerequisites

- .NET SDK 9.x capable of restoring/building the existing solution.
- Network/package cache access for the repository's current NuGet dependencies.
- .NET Framework reference assemblies supplied by the existing package/test
  projects; a live Windows MT5 installation is not required for automated tests.

## 1. Confirm the unchanged canonical contracts

Do not edit `protos/trade.proto`, `protos/position.proto`, or
`protos/symbol_info.proto`. The implementation
reuses:

- `OrderSendService.SendOrder`, `OrderSendRequest`, `OrderSendResponse`,
  `TradeRequest`, and `TradeResult`;
- `PositionsService.GetPositions`, `PositionsGetRequest`,
  `PositionsGetResponse`, and `Position`;
- `SymbolInfoService.GetSymbolInfo`, `SymbolInfoRequest`, `SymbolInfoResponse`,
  and `SymbolInfo`.

Restore once, then run the existing generated-binding/build guard:

```powershell
dotnet restore mt5_grpc_client_csharp/MetaTrader.Grpc.Client.sln
pwsh mt5_grpc_client_csharp/scripts/check-generated.ps1 -Configuration Release
```

Expected: generated C# bindings build from the unchanged proto inputs. No Python
generation or server change is required.

## 2. Implement the public lifecycle surface

Add to `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/`:

- `Mt5GrpcClient.TradeLifecycle.cs` with the six methods from the contract;
- `TradeLifecycleRequests.cs` with operation-specific DTOs;
- `TradeLifecycleResults.cs` with call/execution/batch result types;
- `TradeExecutionClassifier.cs` with the explicit raw-code table;
- `TradeLifecycleExecutor.cs` with pure validation/mapping and the sequential
  batch state machine, plus ticket-driven position/symbol lookups.

Extend existing client internals only as needed to:

- capture one effective default deadline for the whole batch;
- delegate to unchanged `SendOrderAsync` and `GetPositionsAsync` behavior;
- expose an internal scripted transport seam to tests;
- log operation and pair status without full payloads.

Do not change `SendOrderAsync` source behavior.

## 3. Implement documentation examples

Update `mt5_grpc_client_csharp/README.md` and the example projects with independently
runnable snippets for:

1. market and pending open through `OpenOrderAsync`;
2. full (volume omitted) and partial position close using only a ticket;
3. pending-order cancellation using only a ticket;
4. final-state position and pending-order modification;
5. single hedging-only close-by;
6. scoped multiple close-by with per-pair call/execution inspection.

Every example must inspect `CallResult` first and then `ExecutionStatus`/raw
retcode. Warn that transport uncertainty must not be retried automatically and
that the batch is sequential/non-atomic.

Build both examples:

```powershell
dotnet build mt5_grpc_client_csharp/examples/NetStandardClientExample/NetStandardClientExample.csproj -c Release
dotnet build mt5_grpc_client_csharp/examples/NetFramework48ClientExample/NetFramework48ClientExample.csproj -c Release
```

## 4. Focused tests

Add pure/scripted xUnit tests covering:

- every action and field mapping: market/pending open, ticket-driven full/partial
  close, pending-order REMOVE, position/pending modify, and unswapped close-by;
- local invalid inputs and zero send/discovery calls;
- full raw result retention and DONE/DONE_PARTIAL/PLACED/LOCKED/rejected/future
  unknown classification;
- one position lookup plus one symbol-info lookup and at most one send for a
  position close; one send and no lookup for pending-order close; no retries;
- symbol/magic discovery, membership freeze, FIFO plus ticket tie-breaker, BUY as
  primary role, and new-position exclusion;
- rejected pair continuation, uncertain pair withholding/no retry, partial close
  refresh/re-pair, disappeared/ineligible remainder reporting;
- no-op empty batch, discovery/refresh failures, explicit/default deadline capture,
  cancellation stopping new sends, and complete attempted/unattempted accounting;
- caller request/Timestamp/snapshot objects unchanged after invocation.

Run focused projects:

```powershell
dotnet test mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.Tests/MetaTrader.Grpc.Client.Tests.csproj -c Release
dotnet test mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/MetaTrader.Grpc.Client.ContractTests.csproj -c Release
dotnet test mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.CompatibilityTests/MetaTrader.Grpc.Client.CompatibilityTests.csproj -c Release
```

Expected: no broker dependency; scripted call counters prove zero/exactly-one
submission and deterministic ordering.

## 5. Regression, package, and consumer verification

Run the full existing suite and distribution checks:

```powershell
dotnet test mt5_grpc_client_csharp/MetaTrader.Grpc.Client.sln -c Release
pwsh mt5_grpc_client_csharp/scripts/check-package-metadata.ps1 -Configuration Release
pwsh mt5_grpc_client_csharp/scripts/verify-consumer-restore.ps1 -Configuration Release -ModernTfm net9.0
```

Expected:

- existing generic send and generated-contract tests remain green;
- package still contains exactly the existing target/dependency groups;
- clean modern and `net48` consumers compile with the new API and no consumer
  protobuf generation;
- no server/proto package version changes are required.

## 6. Release metadata

Prepare the next additive client minor version (planned 4.3.0):

- update `MetaTrader.Grpc.Client.csproj` version/release notes;
- update `mt5_grpc_client_csharp/CHANGELOG.md` and README;
- keep `ProtoContractIdentity` and `TestedServerVersionRange` unchanged because
  the wire/server contract did not change.

Do not publish from this workflow. Produce and inspect a local Release package:

```powershell
dotnet pack mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/MetaTrader.Grpc.Client.csproj -c Release -p:ContinuousIntegrationBuild=true
```

## 7. Definition of done

| Requirement / criterion | Verification |
|-------------------------|--------------|
| Six intent methods, no caller action | Public surface contract/reflection tests and examples |
| Correct action/identifier/value mapping | Pure request-builder tests |
| Invalid input makes zero calls | Scripted delegate call counters |
| Single operation exactly once, no retry/lookup | Scripted send/position counters |
| Separate call and execution status, raw retention | Classifier/result tests over known and unknown codes |
| FIFO deterministic scoped batch | Discovery/refresh/tie/rejection scripted scenarios |
| Frozen membership under concurrent changes | New-position exclusion and disappeared-ticket tests |
| Cancellation/deadline stops new pairs | Mid-refresh/mid-send cancellation tests with unattempted summary |
| Generic send remains compatible | Existing suite unchanged and green |
| All supported client targets compile | solution, examples, compatibility, package, consumer checks |
