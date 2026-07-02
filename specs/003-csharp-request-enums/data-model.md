# Phase 1 Data Model: C# Request Enum Types

This feature adds no persisted data and no new wire messages. The "entities" are
the C# enum value sets and the companion accessor properties that present the
existing integer request fields as typed values.

## Enum value sets

All four are C# `enum` types with underlying type `int`, declared in namespace
`MetaTrader.Grpc.Client`. Names and values are verbatim from
`specs/003-csharp-request-enums/Mt5Enums.cs` (the authoritative source, FR-009,
FR-012). Because C# enums are open, any `int` — including values not listed here —
is a valid instance and round-trips losslessly (FR-007, FR-008).

### ENUM_TRADE_REQUEST_ACTIONS — Trade Action Value Set
Applies to `TradeRequest.action`. MT5 trade-request operation set.

| Member | Value | Meaning |
|--------|-------|---------|
| `TRADE_ACTION_DEAL` | 1 | Immediate execution (market order) |
| `TRADE_ACTION_PENDING` | 5 | Pending order |
| `TRADE_ACTION_SLTP` | 6 | Modify SL/TP of an open position |
| `TRADE_ACTION_MODIFY` | 7 | Modify an existing order |
| `TRADE_ACTION_REMOVE` | 8 | Delete a pending order |
| `TRADE_ACTION_CLOSE_BY` | 10 | Close a position by an opposite one |

### ENUM_ORDER_TYPE — Order Type Value Set (shared)
Applies to `TradeRequest.type`, `OrderCalcMarginRequest.action`, and
`OrderCalcProfitRequest.action` — one shared representation across trade and
calculation requests (FR-005). All nine members are included. Profit calculation
expects `ORDER_TYPE_BUY` or `ORDER_TYPE_SELL`; this expectation is documented, not
compile-enforced.

| Member | Value |
|--------|-------|
| `ORDER_TYPE_BUY` | 0 |
| `ORDER_TYPE_SELL` | 1 |
| `ORDER_TYPE_BUY_LIMIT` | 2 |
| `ORDER_TYPE_SELL_LIMIT` | 3 |
| `ORDER_TYPE_BUY_STOP` | 4 |
| `ORDER_TYPE_SELL_STOP` | 5 |
| `ORDER_TYPE_BUY_STOP_LIMIT` | 6 |
| `ORDER_TYPE_SELL_STOP_LIMIT` | 7 |
| `ORDER_TYPE_CLOSE_BY` | 8 |

### ENUM_ORDER_TYPE_FILLING — Order Filling Value Set
Applies to `TradeRequest.type_filling`.

| Member | Value |
|--------|-------|
| `ORDER_FILLING_FOK` | 0 |
| `ORDER_FILLING_IOC` | 1 |
| `ORDER_FILLING_RETURN` | 2 |

### ENUM_ORDER_TYPE_TIME — Order Time Value Set
Applies to `TradeRequest.type_time`.

| Member | Value |
|--------|-------|
| `ORDER_TIME_GTC` | 0 |
| `ORDER_TIME_DAY` | 1 |
| `ORDER_TIME_SPECIFIED` | 2 |
| `ORDER_TIME_SPECIFIED_DAY` | 3 |

## Covered request fields → companion accessors

Each covered field keeps its generated `int` property and gains an `Enum`-suffixed
companion property declared on a `partial class` extension of the generated
message. The companion is a thin, allocation-free projection:

```
public EnumType XxxEnum
{
    get => (EnumType)Xxx;      // Xxx is the generated int property
    set => Xxx = (int)value;
}
```

| Message (namespace `Metatrader.V1`) | Raw `int` field | Companion property | Enum type | Proto field # |
|-------------------------------------|-----------------|--------------------|-----------|---------------|
| `TradeRequest` | `Action` | `ActionEnum` | `ENUM_TRADE_REQUEST_ACTIONS` | 1 |
| `TradeRequest` | `Type` | `TypeEnum` | `ENUM_ORDER_TYPE` | 11 |
| `TradeRequest` | `TypeFilling` | `TypeFillingEnum` | `ENUM_ORDER_TYPE_FILLING` | 12 |
| `TradeRequest` | `TypeTime` | `TypeTimeEnum` | `ENUM_ORDER_TYPE_TIME` | 13 |
| `OrderCalcMarginRequest` | `Action` | `ActionEnum` | `ENUM_ORDER_TYPE` | 1 |
| `OrderCalcProfitRequest` | `Action` | `ActionEnum` | `ENUM_ORDER_TYPE` | 1 |

`TradeRequest` is reached by callers through `OrderSendRequest.trade_request` and
`OrderCheckRequest.trade_request`; typing it once covers both send and check paths.

## Invariants / validation rules

- **V1 (value fidelity, FR-003/SC-002)**: `(int)AnyEnumMember` equals the MT5
  numeric value in the tables above; setting a companion property then reading the
  raw `int` yields that value, and vice-versa.
- **V2 (round-trip, FR-007/FR-008/SC-005)**: For any `int n`, setting the raw
  field to `n` and reading the companion property, or setting the companion from
  `(EnumType)n` and reading the raw field, yields `n` unchanged — including `n`
  with no named member. No throw on either direction.
- **V3 (cross-field distinctness, FR-004/SC-003)**: The four enum types are
  distinct C# types; a member of one cannot be assigned to a companion property of
  another without a compile error.
- **V4 (backward compatibility, FR-006/SC-004)**: The raw `int` property is
  unchanged in name, type, and behavior; no existing caller is forced to change.

## State transitions

None. These are immutable value sets and stateless projections over request
fields.
