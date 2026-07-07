# Implementation Plan: C# Request Enum Types (native proto enums)

**Branch**: `003-csharp-request-enums` | **Date**: 2026-07-02 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/003-csharp-request-enums/spec.md`

## Summary

Turn the six request fields that MT5 treats as enums into **native protobuf enum
types** in the shared `.proto` contract, then regenerate the C# client and Python
bindings from it: `TradeRequest.action`, `TradeRequest.type`,
`TradeRequest.type_filling`, `TradeRequest.type_time`,
`OrderCalcMarginRequest.action`, and `OrderCalcProfitRequest.action`.

Enum types and members use the verbatim MT5 names/values from `Mt5Enums.cs`.
`ENUM_ORDER_TYPE` is defined once in `common.proto` (already imported by both
`trade.proto` and `order_calc.proto`) and shared; the three trade-only enums live
in `trade.proto`. proto3 requires a zero-valued first member — the three fields
whose MT5 sets already include 0 (`ORDER_TYPE_BUY`, `ORDER_FILLING_FOK`,
`ORDER_TIME_GTC`) keep it; `ENUM_TRADE_REQUEST_ACTIONS` (no MT5 zero) gets a single
documented `TRADE_ACTION_UNSPECIFIED = 0` sentinel that the server rejects.

This is a **breaking, coordinated contract change**: field numbers and the varint
wire encoding are preserved (proto3 enums encode identically to `int32`, and open
enums preserve unknown values), but C# integer assignments no longer compile and
must migrate to named values. Shipped as `0.2.0` with a documented integer→enum
migration and a proto contract-identity bump; the Python server is re-verified.

## Technical Context

**Language/Version**: proto3 contract; C# (netstandard2.0) client; Python 3 server / proto package
**Primary Dependencies**: Google.Protobuf 3.29.3, Grpc.Tools 2.71.0 (C# codegen); `grpcio-tools` / protoc for Python (`mt5_grpc_proto`)
**Storage**: N/A
**Testing**: xUnit (C# client, contract, compatibility projects); server-side Python tests for FR-014
**Target Platform**: `netstandard2.0` (client, net48 + modern .NET consumers); Windows-first Python server
**Project Type**: Multi-language repo — shared `protos/`, `mt5_grpc_proto` (Python bindings), `mt5_grpc_server`, `mt5_grpc_client_csharp`
**Performance Goals**: No runtime cost change; enum fields encode/decode identically to the prior `int32`
**Constraints**: Preserve field numbers and varint wire compatibility; verbatim MT5 names/values; only one documented sentinel (`TRADE_ACTION_UNSPECIFIED=0`); breaking source change gated behind `0.2.0` + migration doc
**Scale/Scope**: 3 proto files touched (`common.proto`, `trade.proto`, `order_calc.proto`), 4 enum types, 6 retyped fields, 2 regenerated binding sets, 1 server validation rule

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Protocol contract**: **PASS**. The `.proto` change is scoped and documented:
  four enum types added (`ENUM_ORDER_TYPE` in `common.proto`;
  `ENUM_TRADE_REQUEST_ACTIONS`, `ENUM_ORDER_TYPE_FILLING`, `ENUM_ORDER_TYPE_TIME`
  in `trade.proto`), and the six fields retyped from `int32` to their enum. **All
  field numbers are preserved** (`action=1`, `type=11`, `type_filling=12`,
  `type_time=13`, calc `action=1`); no field is renamed or renumbered. Bindings
  are regenerated for both C# and Python. The break is a source-level type change,
  not a wire change; the compatibility decision (breaking → `0.2.0` + explicit
  migration) is recorded in the spec and satisfies the constitution's
  "breaking public contract changes require a major release or an explicit
  migration strategy." See *Summary* and *Protocol and MT5 Contract Impact* in the
  spec, and [research.md](./research.md).
- **MT5 behavior fidelity**: **PASS**. Every named value carries the exact MT5
  numeric value from `specs/003-csharp-request-enums/Mt5Enums.cs`. The only added
  value, `TRADE_ACTION_UNSPECIFIED=0`, is not an MT5 action and is rejected by the
  server rather than executed (FR-014), which *improves* trade safety over the
  prior `int32` default of 0. Return codes and error payloads are unchanged. See
  [data-model.md](./data-model.md).
- **Multi-language type safety**: **PASS**. No Python-only concept; the wire type
  stays varint and is byte-identical to the prior `int32`. proto3 open-enum
  semantics keep out-of-set values representable and round-tripping in every
  language (FR-007, FR-008). In Python, enum fields surface as plain integers, so
  the server reads them unchanged. The one sentinel is documented. See
  [contracts/proto-request-enums.md](./contracts/proto-request-enums.md).
- **Reliability and observability**: **PASS**. The one new behavior is server-side
  rejection of an unset/UNSPECIFIED action, returned as a structured error and
  logged, with no order placed (FR-014). No new streaming, timeout, or secret
  surface. See *Server behavior* in [data-model.md](./data-model.md).
- **Test and release reproducibility**: **PASS**. Verification runs from
  repository scripts: proto regeneration for C# (`dotnet build`) and Python
  (`mt5_grpc_proto` codegen), C# unit/contract tests for value fidelity and
  cross-field type safety, a server test asserting UNSPECIFIED-action rejection
  (SC-009), a server contract check that field values are read identically
  (SC-008), the net48/netstandard2.0 compatibility suite, and a migration
  document. Compile-time cross-field safety is now inherent to the generated
  types; a representative does-not-compile snippet is documented. See
  [research.md](./research.md) Decision 6.

No violations. Complexity Tracking is empty (the breaking change is compliant via
the documented migration strategy and version bump).

## Project Structure

### Documentation (this feature)

```text
specs/003-csharp-request-enums/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── proto-request-enums.md    # proto + generated-surface contract
├── Mt5Enums.cs          # Authoritative MT5 enum source (input)
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
protos/
├── common.proto         # EDIT: add enum ENUM_ORDER_TYPE (shared)
├── trade.proto          # EDIT: add ENUM_TRADE_REQUEST_ACTIONS (+ UNSPECIFIED=0),
│                        #       ENUM_ORDER_TYPE_FILLING, ENUM_ORDER_TYPE_TIME;
│                        #       retype action/type/type_filling/type_time
└── order_calc.proto     # EDIT: retype OrderCalc{Margin,Profit}Request.action to ENUM_ORDER_TYPE

mt5_grpc_proto/           # REGENERATE Python bindings from updated protos
mt5_grpc_server/mt5_grpc_server/imp/
├── trade.py             # EDIT: reject unset/UNSPECIFIED action (FR-014, SC-009)
├── order_check.py       # VERIFY: reads retyped fields with identical semantics (SC-008)
└── order_calc.py        # VERIFY: reads retyped action with identical semantics (SC-008)

mt5_grpc_client_csharp/
├── src/MetaTrader.Grpc.Client/
│   └── MetaTrader.Grpc.Client.csproj   # EDIT: Version 0.1.0 -> 0.2.0; ProtoContractIdentity bump
├── tests/
│   ├── MetaTrader.Grpc.Client.Tests/RequestEnumTests.cs        # NEW: value fidelity, round-trip, open-enum
│   └── MetaTrader.Grpc.Client.CompatibilityTests/...           # EXTEND: named-value usage on net48
├── examples/{NetStandardClientExample,NetFramework48ClientExample}/Program.cs  # EXTEND: named values
├── MIGRATION.md         # NEW: integer -> enum migration guide (FR-006, FR-016)
├── README.md            # EXTEND: named-value usage
└── CHANGELOG.md         # EXTEND: 0.2.0 breaking-change entry
```

**Structure Decision**: The contract is the source of truth, so the change starts
in `protos/`. `ENUM_ORDER_TYPE` goes in `common.proto` because it is shared by
`trade.proto` and `order_calc.proto` (both already `import "common.proto"`), giving
one type identity across trade and calculation requests (FR-015). The three
trade-only enums stay in `trade.proto`. Bindings are regenerated in place for both
C# and Python; the server gains one validation rule; the C# package version and
proto contract identity are bumped together.

## Complexity Tracking

> No Constitution Check violations. The breaking contract change is compliant via
> the documented migration strategy (MIGRATION.md) and the coordinated `0.2.0`
> version bump. This section is intentionally empty.
