# Implementation Plan: Trade Lifecycle Operations

**Branch**: `007-trade-lifecycle-operations` | **Date**: 2026-07-16 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/006-trade-lifecycle-operations/spec.md`

## Summary

Add six intent-focused asynchronous operations to `Mt5GrpcClient` for opening,
closing positions, cancelling pending orders, modifying, closing by, and
automatically processing multiple close-by pairs. Each operation builds a fresh
existing `TradeRequest`, delegates order
submission to the unchanged `SendOrderAsync` path, and returns the existing call
result together with a conservative execution status derived from the raw MT5
return code. The batch operation performs one symbol-scoped discovery, freezes
ticket membership, refreshes only that membership before each deterministic
oldest-buy/oldest-sell pairing decision, submits pairs sequentially without
retry, and retains attempted, unattempted, ineligible, and unpaired outcomes.
Position close accepts only a ticket and optional volume, then performs one
position lookup and one symbol-info lookup to derive the close request under one
effective deadline. Pending-order close accepts only its ticket and maps REMOVE
without a lookup.

This is a C# client-only additive change. `protos/trade.proto`,
`protos/position.proto`, generated bindings, the Python packages, server RPCs,
and the existing generic `SendOrderAsync` source behavior remain unchanged.

## Technical Context

**Language/Version**: C# with `LangVersion=latest`; package targets `netstandard2.0` and `net472`; repository currently builds with the installed .NET SDK 9.x  
**Primary Dependencies**: Existing Google.Protobuf 3.29.3, Grpc.Core.Api 2.71.0, Grpc.Net.Client 2.71.0, Grpc.Tools 2.71.0 (build-only), Microsoft.Bcl.AsyncInterfaces 9.0.0, Microsoft.Extensions.Logging.Abstractions 9.0.0; Grpc.Core 2.46.6 on `net472` only  
**Storage**: N/A; operation and batch state is invocation-local and in memory  
**Testing**: xUnit via `dotnet test`; pure request-mapping/validation/classification tests; scripted transport tests for discovery, refresh, pairing, cancellation, and failures; existing contract, compatibility, package, and example checks  
**Target Platform**: NuGet C# client for .NET implementations compatible with `netstandard2.0`, plus the package's .NET Framework `net472` asset consumed by .NET Framework 4.8 applications  
**Project Type**: Existing C# client SDK with examples and tests; no server or protocol implementation change  
**Performance Goals**: Each dedicated single operation performs at most one `SendOrder` RPC and no retry; position close adds exactly one ticket-filtered `GetPositions` plus one `GetSymbolInfo`, while pending-order close adds no lookup; a batch performs one initial `GetPositions`, at most one symbol-scoped refresh per pairing decision, and one `SendOrder` per attempted pair, all sequentially
**Constraints**: Preserve raw `TradeResult` fields and existing `Mt5GrpcResult` error semantics; no implicit retry; no caller-object or lookup-response mutation; one shared effective deadline and cancellation token per multi-RPC operation; deterministic FIFO/ticket ordering; no proto or generated-source edits; MT5 remains authoritative after lookup snapshots
**Scale/Scope**: Six public methods, operation-specific request/result DTOs, one classifier, one internal executor/test seam, one initial position set per batch, and O(N) retained outcomes/remainders for N frozen positions

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Protocol contract**: PASS. The spec and [research.md](./research.md) identify
  `OrderSendService.SendOrder`, `PositionsService.GetPositions`,
  `SymbolInfoService.GetSymbolInfo`,
  `OrderSendRequest`, `OrderSendResponse`, `TradeRequest`, `TradeResult`,
  `PositionsGetRequest`, `PositionsGetResponse`, `Position`, `SymbolInfoRequest`,
  `SymbolInfoResponse`, and `SymbolInfo`. No `.proto`
  field, number, enum, RPC, generated binding, or server implementation changes;
  compatibility is an additive C# package minor release.
- **MT5 behavior fidelity**: PASS. [research.md](./research.md) and
  [contracts/csharp-trade-lifecycle.md](./contracts/csharp-trade-lifecycle.md)
  map market open/close to `TRADE_ACTION_DEAL`, pending open to
  `TRADE_ACTION_PENDING`, position modification to `TRADE_ACTION_SLTP`, pending
  modification to `TRADE_ACTION_MODIFY`, pending-order cancellation to
  `TRADE_ACTION_REMOVE`, and close-by to
  `TRADE_ACTION_CLOSE_BY` with unswapped ticket roles. MT5 and transport failures
  remain authoritative, and raw responses are retained.
- **Multi-language type safety**: PASS. Shared protobuf types remain unchanged and
  language-neutral. The convenience API is C#-only and preserves existing
  `int64` identifiers, doubles, timestamps, enum numerics, optional presence,
  echoed requests, and repeated response order without changing other clients.
- **Reliability and observability**: PASS. The design prohibits implicit retry,
  distinguishes call success from trade execution, shares an effective deadline
  and cancellation token across a batch, stops new submissions after cancellation
  or deadline, bounds retained state to the frozen discovery set, and logs only
  operation/pair identity and status rather than credentials or full payloads.
- **Test and release reproducibility**: PASS. [quickstart.md](./quickstart.md)
  lists unit, contract, regression, example, generated-binding drift, package
  metadata, and clean-consumer checks. Scripted transports require no broker.

Post-design re-check: PASS. [data-model.md](./data-model.md) defines validation,
immutability boundaries, state transitions, and remainder reasons;
[contracts/csharp-trade-lifecycle.md](./contracts/csharp-trade-lifecycle.md)
freezes the proposed public surface, action/field mappings, result semantics,
retcode categories, batch algorithm, and compatibility guarantees. No
constitution violation or unresolved `NEEDS CLARIFICATION` remains.

## Project Structure

### Documentation (this feature)

```text
specs/006-trade-lifecycle-operations/
|-- plan.md
|-- research.md
|-- data-model.md
|-- quickstart.md
|-- contracts/
|   `-- csharp-trade-lifecycle.md
|-- checklists/
|   `-- requirements.md
`-- tasks.md                 # created later by /speckit-tasks
```

### Source Code (repository root)

```text
mt5_grpc_client_csharp/
|-- src/MetaTrader.Grpc.Client/
|   |-- MetaTrader.Grpc.Client.csproj
|   |-- Mt5GrpcClient.cs
|   |-- Mt5GrpcClient.Trading.cs
|   |-- Mt5GrpcClient.TradeLifecycle.cs          # new public operations
|   |-- TradeLifecycleRequests.cs                # new operation inputs
|   |-- TradeLifecycleResults.cs                 # new result/status types
|   |-- TradeExecutionClassifier.cs              # new raw-retcode mapping
|   `-- TradeLifecycleExecutor.cs                # new mapping/batch/test seam
|-- tests/
|   |-- MetaTrader.Grpc.Client.Tests/
|   |   |-- TradeLifecycleMappingTests.cs        # new
|   |   |-- TradeExecutionClassifierTests.cs     # new
|   |   `-- MultipleCloseByTests.cs              # new scripted workflows
|   |-- MetaTrader.Grpc.Client.ContractTests/
|   |   `-- TradeLifecycleSurfaceTests.cs        # new public/compat contract
|   `-- MetaTrader.Grpc.Client.CompatibilityTests/
|       `-- NetFramework48ReferenceTests.cs      # extend compile coverage
|-- examples/
|   |-- NetStandardClientExample/Program.cs      # six runnable method examples
|   `-- NetFramework48ClientExample/Program.cs   # surface compatibility
|-- README.md
|-- CHANGELOG.md
`-- scripts/
    |-- check-generated.ps1
    |-- check-package-metadata.ps1
    `-- verify-consumer-restore.ps1

protos/
|-- trade.proto                  # reused, unchanged
`-- position.proto               # reused, unchanged
```

**Structure Decision**: Extend the existing partial `Mt5GrpcClient` in the
current C# package. Keep public lifecycle DTOs/results beside the client and put
mapping, classification, and batch orchestration behind an internal executor
whose send/position delegates can be scripted in unit tests. Do not create a new
project, server component, proto contract, or generated-code directory.

## Complexity Tracking

No constitution violations.
