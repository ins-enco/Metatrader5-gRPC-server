# Implementation Plan: C# Request Enum Types

**Branch**: `003-csharp-request-enums` | **Date**: 2026-07-02 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/003-csharp-request-enums/spec.md`

## Summary

Give C# callers named, compile-checked values for the six request fields that are
semantically MT5 enums but are currently exposed as raw `int32` on the wire:
`TradeRequest.action`, `TradeRequest.type`, `TradeRequest.type_filling`,
`TradeRequest.type_time`, `OrderCalcMarginRequest.action`, and
`OrderCalcProfitRequest.action`.

Technical approach: add four C# `enum` types with the verbatim MT5 names and
numeric values from the authoritative source (`Mt5Enums.cs`) into the client
library namespace, then expose them through `Enum`-suffixed companion properties
declared on `partial class` extensions of the generated request messages. The
raw `int` properties stay untouched (backward compatibility) and the companion
properties cast to/from `int`, so undefined future MT5 values still round-trip.
The `.proto` contract is unchanged — this is a C#-client-only, backward-compatible
addition with no cross-language impact.

## Technical Context

**Language/Version**: C# (LangVersion default for netstandard2.0), targeting the existing client library
**Primary Dependencies**: Google.Protobuf 3.29.3, Grpc.Net.Client / Grpc.Core.Api 2.71.0, Grpc.Tools 2.71.0 (codegen)
**Storage**: N/A (in-memory request construction only)
**Testing**: xUnit across existing `MetaTrader.Grpc.Client.Tests`, `.ContractTests`, `.CompatibilityTests` projects
**Target Platform**: `netstandard2.0` (library), exercised by .NET Framework 4.8 and modern .NET consumers
**Project Type**: Multi-language repo; this feature touches only the C# client library (`mt5_grpc_client_csharp/`)
**Performance Goals**: No runtime cost beyond an `int`↔`enum` cast; no allocation, no I/O added
**Constraints**: Must not edit generated protobuf code; must not change the `.proto` contract; must preserve `netstandard2.0` compatibility; existing integer callers must compile unchanged
**Scale/Scope**: 4 enum types (6 members / 9 members / 3 members / 4 members), 6 companion properties across 3 generated request messages

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Protocol contract**: **PASS**. No `.proto` file changes. The request fields
  remain `int32` with their existing field numbers (`action=1`, `type=11`,
  `type_filling=12`, `type_time=13`, `OrderCalc*.action=1`). No field is added,
  renamed, or renumbered; generated bindings are byte-identical after rebuild.
  See *Summary* and *Protocol and MT5 Contract Impact* in the spec. The enums and
  companion properties are a C#-only presentation layer over unchanged fields.
- **MT5 behavior fidelity**: **PASS**. Every named value carries the exact MT5
  numeric value from the authoritative `specs/003-csharp-request-enums/Mt5Enums.cs`
  (e.g. `TRADE_ACTION_DEAL=1`, `ORDER_TYPE_SELL=1`, `ORDER_FILLING_IOC=1`,
  `ORDER_TIME_DAY=1`). The transmitted `int` equals the value a correct
  hand-written integer request would carry, so server-observed behavior and MT5
  return codes are unchanged. See [research.md](./research.md) value-mapping table
  and [data-model.md](./data-model.md).
- **Multi-language type safety**: **PASS**. No Python-only or C#-only wire
  concept is introduced; the wire type stays `int32`. Values outside the named set
  remain representable and round-trip without loss (C# enum casts are unchecked).
  No sentinel values are added. Python server and other-language clients are
  untouched. See *Cross-Language Type Notes* in the spec and the round-trip
  contract in [contracts/csharp-request-enums.md](./contracts/csharp-request-enums.md).
- **Reliability and observability**: **PASS (mostly N/A)**. No new RPC, stream,
  timeout, cancellation, logging, or secret-handling surface is introduced. The
  change is a pure compile-time typing layer with no runtime failure modes beyond
  what the existing integer path already has. Undefined values do not throw on
  read or write (FR-008).
- **Test and release reproducibility**: **PASS**. Verification listed below runs
  from repository scripts: value-mapping unit tests, round-trip tests for
  undefined values, backward-compatibility (integer path unchanged) tests, and the
  existing `netstandard2.0` / net48 compatibility suite. The compile-time
  rejection of cross-field values (SC-003) is asserted by documented
  "does-not-compile" snippets rather than an automated negative-compile test;
  justification recorded in [research.md](./research.md).

No violations. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/003-csharp-request-enums/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── csharp-request-enums.md   # C# public API contract
├── Mt5Enums.cs          # Authoritative MT5 enum source (input)
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
mt5_grpc_client_csharp/
├── src/MetaTrader.Grpc.Client/
│   ├── Enums/
│   │   ├── Mt5RequestEnums.cs              # NEW: ENUM_TRADE_REQUEST_ACTIONS,
│   │   │                                   #      ENUM_ORDER_TYPE,
│   │   │                                   #      ENUM_ORDER_TYPE_FILLING,
│   │   │                                   #      ENUM_ORDER_TYPE_TIME
│   │   └── RequestEnumAccessors.cs         # NEW: partial-class companion
│   │                                       #      properties on the generated
│   │                                       #      request messages
│   ├── Mt5GrpcClient.Trading.cs            # unchanged
│   └── ...                                 # other existing files unchanged
├── tests/
│   ├── MetaTrader.Grpc.Client.Tests/
│   │   └── RequestEnumTests.cs             # NEW: value mapping, round-trip,
│   │                                       #      backward-compat, undefined values
│   └── MetaTrader.Grpc.Client.CompatibilityTests/
│       └── NetFramework48ReferenceTests.cs # EXTEND: named-value usage on net48
├── examples/
│   ├── NetStandardClientExample/Program.cs # EXTEND: named-value trade/calc build
│   └── NetFramework48ClientExample/Program.cs
├── README.md                               # EXTEND: named-value usage section
└── CHANGELOG.md                            # EXTEND: entry for this addition
```

**Structure Decision**: The feature lives entirely inside the existing C# client
library project (`mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client`). New enum
types and their companion accessors go under a new `Enums/` folder in that
project. No new project is created; existing test and example projects are
extended. The generated protobuf code under `obj/.../Trade.cs` and
`obj/.../OrderCalc.cs` is left untouched — the companion properties are declared
in separate `partial class` files in the generated `Metatrader.V1` namespace.

## Complexity Tracking

> No Constitution Check violations. This section is intentionally empty.
