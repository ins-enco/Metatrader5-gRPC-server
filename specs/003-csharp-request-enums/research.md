# Phase 0 Research: C# Request Enum Types (native proto enums)

The compatibility posture was reopened in the 2026-07-02 revision clarification
session. The feature now delivers native protobuf enum-typed request fields on the
shared `.proto` contract. The decisions below turn that posture into an
implementation approach. No `NEEDS CLARIFICATION` markers remain.

## Decision 1 — Retype the fields to proto enums, not a C# overlay

**Decision**: Change the six request fields in the `.proto` from `int32` to
protobuf `enum` types and regenerate all bindings, rather than adding a C#-only
companion-property overlay.

**Rationale**:
- The fields become the enum type directly, so cross-field type safety (FR-004) is
  inherent — no dual surface, no companion naming, no risk of the raw `int` and
  typed accessor drifting.
- proto3 enums encode on the wire as varint, **identical** to `int32`, so field
  numbers and wire compatibility are preserved and no serialized data migrates.
- proto3 enums are *open*: unknown numeric values are preserved on parse and
  re-serialize losslessly, satisfying FR-007 and FR-008 in every language.

**Cost accepted**: source compatibility breaks for C# integer callers (`req.Action
= 1` no longer compiles). This is the deliberate, clarified trade-off, mitigated by
a migration guide (MIGRATION.md) and a `0.2.0` version bump (Decisions 5–6).

**Alternatives considered**: the C# companion-property overlay (prior plan) —
preserved integer callers but left a permanent dual `int`/enum surface and no true
type on the field; superseded by this revision. Hand-editing generated code —
non-viable (regenerated every build) and constitution-prohibited.

## Decision 2 — Where each enum is defined

**Decision**:
- `ENUM_ORDER_TYPE` → **`common.proto`** (package `metatrader.v1`). It is shared by
  `TradeRequest.type`, `OrderCalcMarginRequest.action`, and
  `OrderCalcProfitRequest.action`. `common.proto` is already imported by both
  `trade.proto` and `order_calc.proto`, so no new imports are needed (FR-015).
- `ENUM_TRADE_REQUEST_ACTIONS`, `ENUM_ORDER_TYPE_FILLING`, `ENUM_ORDER_TYPE_TIME` →
  **`trade.proto`**. They are used only by `TradeRequest`.

**Rationale**: keeps the shared type in the shared file (single identity) and the
trade-only types local. No new `.proto` file is required, minimizing build-glob and
Python-packaging churn.

**Enum value name collisions**: proto3 uses C++ scoping — top-level enum value
names must be unique within the package. The four sets (`ORDER_TYPE_*`,
`TRADE_ACTION_*`, `ORDER_FILLING_*`, `ORDER_TIME_*`) have distinct prefixes and do
not collide with each other or with any existing package-level enum values (the
contract currently defines no proto enums). No renaming needed.

## Decision 3 — proto3 mandatory zero value

**Decision**: Add `TRADE_ACTION_UNSPECIFIED = 0` to `ENUM_TRADE_REQUEST_ACTIONS`
only. `ENUM_ORDER_TYPE`, `ENUM_ORDER_TYPE_FILLING`, and `ENUM_ORDER_TYPE_TIME`
already have an MT5-defined 0 member (`ORDER_TYPE_BUY`, `ORDER_FILLING_FOK`,
`ORDER_TIME_GTC`) and use it as their required zero unchanged.

**Rationale**: proto3 requires the first enum member to be 0. MT5 defines no
action `0` (actions are 1,5,6,7,8,10), so a sentinel is unavoidable there; it is
the single documented exception permitted by FR-009. For the other three, MT5's
own 0 satisfies proto3 with no invented value. The default/unset value for those
three equals BUY/FOK/GTC, which is **identical to today's `int32` behaviour**
(unset = 0), so this introduces no new ambiguity.

**Trade-safety follow-through**: `TRADE_ACTION_UNSPECIFIED` (and any unset action)
MUST be rejected server-side with a structured error and place no order (FR-014,
SC-009). This is stricter and safer than the prior `int32` field, where an unset
action silently defaulted to 0.

**Alternatives considered**: forcing `*_UNSPECIFIED=0` onto all four enums —
impossible without renumbering BUY/FOK/GTC off 0, which would change MT5 values
(forbidden by FR-009). Rejected.

## Decision 4 — Namespace of the generated enums

**Decision**: The enums are generated into the protobuf package namespace —
`metatrader.v1`, surfaced in C# as `Metatrader.V1` (there is no `csharp_namespace`
option in the protos, so C# namespace derives from the package). This updates the
earlier clarification (Q1) that placed hand-written enums in the client library
namespace.

**Rationale**: with native proto enums, the type location is determined by the
contract, not hand-authored. The verbatim MT5 names (FR-012) still let callers
cross-reference MQL5 docs; only the containing namespace changes, and it sits
alongside the request messages the caller already uses (`Metatrader.V1`).

## Decision 5 — Versioning and migration

**Decision**: Release as C# package `0.2.0` (pre-1.0 breaking bump) with a matching
`ProtoContractIdentity` update, plus `MIGRATION.md` documenting the integer→enum
conversion (named values, or `(EnumType)value` casts for unnamed values).

**Rationale**: the package is `0.1.0`; under pre-1.0 semver a breaking change is a
minor bump, and the constitution accepts breaking contract changes with an explicit
migration strategy. Versioning of package and contract must be coordinated
(Project Constraints), hence the paired contract-identity bump.

## Decision 6 — Verifying cross-field safety (SC-003)

**Decision**: With native enum fields, assigning the wrong enum type (or a raw int)
to a field is a compile error inherently. This is documented with a representative
does-not-compile snippet in the contract/quickstart; positive behaviors (value
fidelity, round-trip of unknown values, server rejection of UNSPECIFIED) are
automated.

**Rationale**: as before, there is no ergonomic in-suite assertion for
"must-not-compile" without a Roslyn harness, which is disproportionate here. Per
constitution principle V, the un-automatable check is documented explicitly.

## Authoritative value mapping

From `specs/003-csharp-request-enums/Mt5Enums.cs` (unchanged from the source; the
`TRADE_ACTION_UNSPECIFIED=0` row is the only added, non-MT5 value):

| Enum | Member | Value | Home proto |
|------|--------|-------|-----------|
| `ENUM_TRADE_REQUEST_ACTIONS` | `TRADE_ACTION_UNSPECIFIED` | 0 | trade.proto |
| | `TRADE_ACTION_DEAL` | 1 | |
| | `TRADE_ACTION_PENDING` | 5 | |
| | `TRADE_ACTION_SLTP` | 6 | |
| | `TRADE_ACTION_MODIFY` | 7 | |
| | `TRADE_ACTION_REMOVE` | 8 | |
| | `TRADE_ACTION_CLOSE_BY` | 10 | |
| `ENUM_ORDER_TYPE` | `ORDER_TYPE_BUY` | 0 | common.proto |
| | `ORDER_TYPE_SELL` | 1 | |
| | `ORDER_TYPE_BUY_LIMIT` | 2 | |
| | `ORDER_TYPE_SELL_LIMIT` | 3 | |
| | `ORDER_TYPE_BUY_STOP` | 4 | |
| | `ORDER_TYPE_SELL_STOP` | 5 | |
| | `ORDER_TYPE_BUY_STOP_LIMIT` | 6 | |
| | `ORDER_TYPE_SELL_STOP_LIMIT` | 7 | |
| | `ORDER_TYPE_CLOSE_BY` | 8 | |
| `ENUM_ORDER_TYPE_FILLING` | `ORDER_FILLING_FOK` | 0 | trade.proto |
| | `ORDER_FILLING_IOC` | 1 | |
| | `ORDER_FILLING_RETURN` | 2 | |
| `ENUM_ORDER_TYPE_TIME` | `ORDER_TIME_GTC` | 0 | trade.proto |
| | `ORDER_TIME_DAY` | 1 | |
| | `ORDER_TIME_SPECIFIED` | 2 | |
| | `ORDER_TIME_SPECIFIED_DAY` | 3 | |
