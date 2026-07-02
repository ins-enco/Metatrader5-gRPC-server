# Migration Guide: 0.1.x → 0.2.0 (request enum types)

In `0.2.0` the request fields that MT5 treats as enumerations became **native
protobuf enum types**. Six fields changed from `int` to a strongly typed enum:

| Message | Property | Was (0.1.x) | Now (0.2.0) |
|---------|----------|-------------|-------------|
| `TradeRequest` | `Action` | `int` | `ENUM_TRADE_REQUEST_ACTIONS` |
| `TradeRequest` | `Type` | `int` | `ENUM_ORDER_TYPE` |
| `TradeRequest` | `TypeFilling` | `int` | `ENUM_ORDER_TYPE_FILLING` |
| `TradeRequest` | `TypeTime` | `int` | `ENUM_ORDER_TYPE_TIME` |
| `OrderCalcMarginRequest` | `Action` | `int` | `ENUM_ORDER_TYPE` |
| `OrderCalcProfitRequest` | `Action` | `int` | `ENUM_ORDER_TYPE` |

**This is a source-breaking change.** Code that assigned raw integers (e.g.
`Action = 1`) no longer compiles (CS0029). The **wire format is unchanged** —
proto3 enums encode as the same varint the `int32` fields used, so no data,
serialized payloads, or other-language peers need to migrate, and the transmitted
numeric value for an equivalent selection is identical.

All types live in the `Metatrader.V1` namespace (add `using Metatrader.V1;`).

> **Naming note**: the protobuf C# generator converts the verbatim MT5 member
> names to PascalCase (e.g. `TRADE_ACTION_DEAL` → `TradeActionDeal`). The wire
> name is preserved for cross-referencing MQL5 docs. Use the PascalCase form in
> C# code.

## Replace integers with named values

Each named value transmits the **identical** numeric value it replaced.

### `TradeRequest`

```csharp
// Before (0.1.x):
var t = new TradeRequest { Action = 1, Type = 0, TypeFilling = 1, TypeTime = 0 };

// After (0.2.0):
var t = new TradeRequest
{
    Action      = ENUM_TRADE_REQUEST_ACTIONS.TradeActionDeal,  // 1
    Type        = ENUM_ORDER_TYPE.OrderTypeBuy,                // 0
    TypeFilling = ENUM_ORDER_TYPE_FILLING.OrderFillingIoc,     // 1
    TypeTime    = ENUM_ORDER_TYPE_TIME.OrderTimeGtc,           // 0
};
```

### Calculation requests

```csharp
// Before: new OrderCalcMarginRequest { Action = 0 };
var margin = new OrderCalcMarginRequest { Action = ENUM_ORDER_TYPE.OrderTypeBuy };   // 0

// Before: new OrderCalcProfitRequest { Action = 1 };
var profit = new OrderCalcProfitRequest { Action = ENUM_ORDER_TYPE.OrderTypeSell };  // 1
```

## Complete value reference

### `ENUM_TRADE_REQUEST_ACTIONS` (`TradeRequest.Action`)

| Integer (0.1.x) | Named value (0.2.0) |
|-----------------|---------------------|
| *(unset)* / `0` | `TradeActionUnspecified` — **rejected by the server**, do not send |
| `1` | `TradeActionDeal` |
| `5` | `TradeActionPending` |
| `6` | `TradeActionSltp` |
| `7` | `TradeActionModify` |
| `8` | `TradeActionRemove` |
| `10` | `TradeActionCloseBy` |

### `ENUM_ORDER_TYPE` (`TradeRequest.Type`, calc `Action`)

| Integer | Named value |
|---------|-------------|
| `0` | `OrderTypeBuy` |
| `1` | `OrderTypeSell` |
| `2` | `OrderTypeBuyLimit` |
| `3` | `OrderTypeSellLimit` |
| `4` | `OrderTypeBuyStop` |
| `5` | `OrderTypeSellStop` |
| `6` | `OrderTypeBuyStopLimit` |
| `7` | `OrderTypeSellStopLimit` |
| `8` | `OrderTypeCloseBy` |

### `ENUM_ORDER_TYPE_FILLING` (`TradeRequest.TypeFilling`)

| Integer | Named value |
|---------|-------------|
| `0` | `OrderFillingFok` |
| `1` | `OrderFillingIoc` |
| `2` | `OrderFillingReturn` |

### `ENUM_ORDER_TYPE_TIME` (`TradeRequest.TypeTime`)

| Integer | Named value |
|---------|-------------|
| `0` | `OrderTimeGtc` |
| `1` | `OrderTimeDay` |
| `2` | `OrderTimeSpecified` |
| `3` | `OrderTimeSpecifiedDay` |

## Values with no named member (future MT5 builds)

proto3 enums are *open*: a numeric value with no named member is preserved on
set, serialize, parse, and read without loss or exception. If MT5 adds a value
this library predates, cast the integer explicitly:

```csharp
var req = new TradeRequest { Action = (ENUM_TRADE_REQUEST_ACTIONS)99 };
int raw = (int)req.Action;   // == 99, round-trips through serialization, no throw
```

## Default / unset action

Leaving `Action` unset is `TradeActionUnspecified` (0), which MT5 does not define.
The server rejects it with a structured error and places no order — always set an
explicit action. This is stricter (and safer) than the prior `int` field, where
an unset action silently defaulted to `0`.
