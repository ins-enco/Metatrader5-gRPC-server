# Phase 0 Research: C# Request Enum Types

All spec clarifications were resolved in the 2026-07-02 clarification session
(enum naming, request-only scope, full `ENUM_ORDER_TYPE` for calc actions). No
`NEEDS CLARIFICATION` markers remained after loading the spec. The research below
records the technical decisions that turn those clarifications into an
implementation approach.

## Decision 1 — How to expose the enums without breaking integer callers

**Decision**: Keep the generated `int` properties exactly as they are and add an
`Enum`-suffixed companion property for each covered field on a `partial class`
extension of the generated request message. Getter returns `(EnumType)RawInt`;
setter assigns `RawInt = (int)value`.

**Rationale**:
- The generated protobuf messages are `public sealed partial class` in namespace
  `Metatrader.V1` (verified in `obj/Debug/netstandard2.0/Trade.cs` and
  `OrderCalc.cs`). A separate `partial class` file in the same namespace can add
  members without editing generated code — satisfying the constitution's "generated
  code MUST NOT be edited as the source of truth."
- Leaving the raw `int` property untouched satisfies FR-006 / SC-004: existing
  integer-based code compiles and transmits identical values with zero source
  changes.
- A C# `enum` cast (`(EnumType)someInt` / `(int)someEnumValue`) is unchecked — any
  integer, including values not defined in the enum, converts without throwing.
  This satisfies FR-007 (send future/unknown MT5 values) and FR-008 (read a value
  with no named entry without throwing or altering it) for free.

**Alternatives considered**:
- *Fluent extension methods* (`request.WithAction(...)` + `request.GetAction()`):
  works and keeps the generated surface visually clean, but companion properties
  give better IDE completion on assignment (`req.ActionEnum = ` immediately lists
  members), which Acceptance Scenario 3 calls out. Rejected in favor of properties.
- *Replacing the `int` property with an `enum` property*: would break FR-006 and
  FR-007 (can't send undefined values ergonomically) and would require editing
  generated code. Rejected.
- *Changing the `.proto` field to a proto `enum`*: violates the backward-compat
  decision, forces proto regeneration and cross-language change, and breaks the
  wire-level `int32` guarantee. Rejected (explicitly out of scope per spec).

## Decision 2 — Enum type and member names, and namespace

**Decision**: Deliver exactly four enum types, named verbatim from the
authoritative source, in the client library namespace `MetaTrader.Grpc.Client`:
`ENUM_TRADE_REQUEST_ACTIONS`, `ENUM_ORDER_TYPE`, `ENUM_ORDER_TYPE_FILLING`,
`ENUM_ORDER_TYPE_TIME`. Members use the verbatim MT5 names and numeric values.

**Rationale**: Clarification Q1 fixed verbatim MT5 names placed in the client
library namespace (not `MtApi5`) so callers can cross-reference official MT5/MQL5
docs directly (FR-012). Only the four enums actually used by the six covered
request fields are delivered — the other enums in `Mt5Enums.cs` (symbol/account/
order-property/position/deal/transaction) are response-side or out of scope
(request-only scope per clarification Q2). The source `Mt5Enums.cs` declares its
types in `namespace MtApi5`; we re-declare only the four needed types under
`MetaTrader.Grpc.Client`, matching FR-012.

**Authoritative value mapping** (from `specs/003-csharp-request-enums/Mt5Enums.cs`):

| Enum type | Member | Value |
|-----------|--------|-------|
| `ENUM_TRADE_REQUEST_ACTIONS` | `TRADE_ACTION_DEAL` | 1 |
| | `TRADE_ACTION_PENDING` | 5 |
| | `TRADE_ACTION_SLTP` | 6 |
| | `TRADE_ACTION_MODIFY` | 7 |
| | `TRADE_ACTION_REMOVE` | 8 |
| | `TRADE_ACTION_CLOSE_BY` | 10 |
| `ENUM_ORDER_TYPE` | `ORDER_TYPE_BUY` | 0 |
| | `ORDER_TYPE_SELL` | 1 |
| | `ORDER_TYPE_BUY_LIMIT` | 2 |
| | `ORDER_TYPE_SELL_LIMIT` | 3 |
| | `ORDER_TYPE_BUY_STOP` | 4 |
| | `ORDER_TYPE_SELL_STOP` | 5 |
| | `ORDER_TYPE_BUY_STOP_LIMIT` | 6 |
| | `ORDER_TYPE_SELL_STOP_LIMIT` | 7 |
| | `ORDER_TYPE_CLOSE_BY` | 8 |
| `ENUM_ORDER_TYPE_FILLING` | `ORDER_FILLING_FOK` | 0 |
| | `ORDER_FILLING_IOC` | 1 |
| | `ORDER_FILLING_RETURN` | 2 |
| `ENUM_ORDER_TYPE_TIME` | `ORDER_TIME_GTC` | 0 |
| | `ORDER_TIME_DAY` | 1 |
| | `ORDER_TIME_SPECIFIED` | 2 |
| | `ORDER_TIME_SPECIFIED_DAY` | 3 |

## Decision 3 — Companion property names

**Decision**: Suffix the raw field's C# name with `Enum`:

| Message | Raw field (unchanged) | Companion property | Type |
|---------|-----------------------|--------------------|------|
| `TradeRequest` | `Action` (`int`) | `ActionEnum` | `ENUM_TRADE_REQUEST_ACTIONS` |
| `TradeRequest` | `Type` (`int`) | `TypeEnum` | `ENUM_ORDER_TYPE` |
| `TradeRequest` | `TypeFilling` (`int`) | `TypeFillingEnum` | `ENUM_ORDER_TYPE_FILLING` |
| `TradeRequest` | `TypeTime` (`int`) | `TypeTimeEnum` | `ENUM_ORDER_TYPE_TIME` |
| `OrderCalcMarginRequest` | `Action` (`int`) | `ActionEnum` | `ENUM_ORDER_TYPE` |
| `OrderCalcProfitRequest` | `Action` (`int`) | `ActionEnum` | `ENUM_ORDER_TYPE` |

**Rationale**: A distinct name is required because the raw property name is taken
by the generated code and both must coexist (backward compat). The `Enum` suffix
is predictable, sorts next to the raw field in completion lists, and reads
clearly at call sites (`req.ActionEnum = ENUM_TRADE_REQUEST_ACTIONS.TRADE_ACTION_DEAL`).

**Alternatives considered**: semantic names (`OrderType`, `FillingPolicy`, …) —
rejected because they drift from the wire field names and make the raw↔typed
correspondence less obvious. `Typed` suffix — equivalent; `Enum` chosen for brevity.

## Decision 4 — Compile-time cross-field rejection (SC-003) verification

**Decision**: Distinct enum types per field give the compile-time guarantee for
free (assigning `ENUM_ORDER_TYPE` to `ActionEnum` is `CS0029`). This is verified
by documented "does-not-compile" snippets in the contract and quickstart rather
than an automated negative-compile test.

**Rationale**: There is no ergonomic in-suite xUnit assertion for "this line must
fail to compile" without a Roslyn compilation harness, which is disproportionate
for this feature. Per constitution principle V, a check that cannot practically
run in the standard suite is documented explicitly. The positive guarantees
(correct values, round-trip, backward compat) are all automated.

**Alternatives considered**: a Roslyn `CSharpCompilation` test that asserts a
diagnostic — deferred as unnecessary complexity; can be added later if desired.

## Decision 5 — netstandard2.0 / .NET Framework 4.8 compatibility (FR-011, SC-007)

**Decision**: No new language or framework features are required — C# `enum`
types and `partial class` properties are fully supported on `netstandard2.0`.
Coverage is added to the existing `MetaTrader.Grpc.Client.CompatibilityTests`
(net48 reference tests) so named-value usage is exercised on the compatibility
target.

**Rationale**: The library already targets `netstandard2.0` and is consumed by a
net48 example and compatibility test project. Nothing in the design depends on a
newer TFM.
