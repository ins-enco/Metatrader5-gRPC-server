# Phase 1 Data Model: C# Request Enum Types (native proto enums)

This feature adds no persisted data. The "entities" are four protobuf `enum` types
added to the shared contract and the six request fields retyped to use them. All
names/values are verbatim from `specs/003-csharp-request-enums/Mt5Enums.cs` except
the single proto3-mandated `TRADE_ACTION_UNSPECIFIED = 0` sentinel.

## Enum types (proto3)

Defined in package `metatrader.v1` (C# namespace `Metatrader.V1`; Python: plain
integer values). proto3 enums are open — any int, including values with no member,
is representable and round-trips (FR-007, FR-008).

### ENUM_ORDER_TYPE — `common.proto` (shared)
Used by `TradeRequest.type`, `OrderCalcMarginRequest.action`,
`OrderCalcProfitRequest.action` (FR-005, FR-015). First member is 0 (satisfies
proto3 with a real MT5 value).

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

### ENUM_TRADE_REQUEST_ACTIONS — `trade.proto`
Used by `TradeRequest.action`. MT5 defines no action 0, so a documented sentinel
is added at 0 and rejected by the server (FR-009, FR-014).

| Member | Value | Note |
|--------|-------|------|
| `TRADE_ACTION_UNSPECIFIED` | 0 | Added sentinel; not an MT5 action; server rejects |
| `TRADE_ACTION_DEAL` | 1 | Market order |
| `TRADE_ACTION_PENDING` | 5 | Pending order |
| `TRADE_ACTION_SLTP` | 6 | Modify SL/TP |
| `TRADE_ACTION_MODIFY` | 7 | Modify order |
| `TRADE_ACTION_REMOVE` | 8 | Delete pending order |
| `TRADE_ACTION_CLOSE_BY` | 10 | Close by opposite |

### ENUM_ORDER_TYPE_FILLING — `trade.proto`
Used by `TradeRequest.type_filling`.

| Member | Value |
|--------|-------|
| `ORDER_FILLING_FOK` | 0 |
| `ORDER_FILLING_IOC` | 1 |
| `ORDER_FILLING_RETURN` | 2 |

### ENUM_ORDER_TYPE_TIME — `trade.proto`
Used by `TradeRequest.type_time`.

| Member | Value |
|--------|-------|
| `ORDER_TIME_GTC` | 0 |
| `ORDER_TIME_DAY` | 1 |
| `ORDER_TIME_SPECIFIED` | 2 |
| `ORDER_TIME_SPECIFIED_DAY` | 3 |

## Retyped request fields

Field numbers and wire encoding are unchanged; only the declared type changes.

| Message | Field | # | Was | Now |
|---------|-------|---|-----|-----|
| `TradeRequest` | `action` | 1 | `int32` | `ENUM_TRADE_REQUEST_ACTIONS` |
| `TradeRequest` | `type` | 11 | `int32` | `ENUM_ORDER_TYPE` |
| `TradeRequest` | `type_filling` | 12 | `int32` | `ENUM_ORDER_TYPE_FILLING` |
| `TradeRequest` | `type_time` | 13 | `int32` | `ENUM_ORDER_TYPE_TIME` |
| `OrderCalcMarginRequest` | `action` | 1 | `int32` | `ENUM_ORDER_TYPE` |
| `OrderCalcProfitRequest` | `action` | 1 | `int32` | `ENUM_ORDER_TYPE` |

`TradeRequest` is reached via `OrderSendRequest.trade_request` and
`OrderCheckRequest.trade_request`; retyping it once covers send and check.

## Invariants / validation rules

- **V1 (value fidelity, FR-003/SC-002)**: each named member's numeric value equals
  the MT5 value above; the transmitted varint equals that value.
- **V2 (round-trip, FR-007/FR-008/SC-005)**: any numeric value, including one with
  no named member, round-trips on set/serialize/parse/read without throw or
  change (proto3 open enums).
- **V3 (cross-field distinctness, FR-004/SC-003)**: the four enum types are
  distinct; assigning one to a field of another type — or assigning a raw int — is
  a compile error in C#.
- **V4 (wire compatibility, FR-006/Compatibility Decision)**: encoding is varint,
  byte-identical to the prior `int32`; existing serialized payloads and other-
  language peers are unaffected.

## Server behavior (FR-014, SC-009)

`mt5_grpc_server/.../imp/trade.py` MUST reject a trade request whose `action` is
unset or `TRADE_ACTION_UNSPECIFIED (0)` with a structured `Error` and place no
order. `order_check.py` and `order_calc.py` read the retyped fields as integers
(Python protobuf enum semantics) and MUST behave identically to the prior `int32`
reads (SC-008). No other server logic changes.

## State transitions

None. Immutable value sets and stateless field typing.
