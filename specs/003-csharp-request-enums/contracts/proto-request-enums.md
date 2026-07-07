# Contract: Proto Request Enums

This feature changes the shared `.proto` contract and, through regeneration, the
C# and Python surfaces. The authoritative contract is the proto below; the
generated-surface guarantees follow.

## Proto changes

### `common.proto` — add shared enum

```proto
// Order type (shared by trade requests and order calculations)
enum ENUM_ORDER_TYPE {
  ORDER_TYPE_BUY             = 0;
  ORDER_TYPE_SELL            = 1;
  ORDER_TYPE_BUY_LIMIT       = 2;
  ORDER_TYPE_SELL_LIMIT      = 3;
  ORDER_TYPE_BUY_STOP        = 4;
  ORDER_TYPE_SELL_STOP       = 5;
  ORDER_TYPE_BUY_STOP_LIMIT  = 6;
  ORDER_TYPE_SELL_STOP_LIMIT = 7;
  ORDER_TYPE_CLOSE_BY        = 8;
}
```

### `trade.proto` — add trade-only enums, retype fields

```proto
enum ENUM_TRADE_REQUEST_ACTIONS {
  TRADE_ACTION_UNSPECIFIED = 0;  // proto3-required zero; not an MT5 action; server rejects
  TRADE_ACTION_DEAL        = 1;
  TRADE_ACTION_PENDING     = 5;
  TRADE_ACTION_SLTP        = 6;
  TRADE_ACTION_MODIFY      = 7;
  TRADE_ACTION_REMOVE      = 8;
  TRADE_ACTION_CLOSE_BY    = 10;
}

enum ENUM_ORDER_TYPE_FILLING {
  ORDER_FILLING_FOK    = 0;
  ORDER_FILLING_IOC    = 1;
  ORDER_FILLING_RETURN = 2;
}

enum ENUM_ORDER_TYPE_TIME {
  ORDER_TIME_GTC           = 0;
  ORDER_TIME_DAY           = 1;
  ORDER_TIME_SPECIFIED     = 2;
  ORDER_TIME_SPECIFIED_DAY = 3;
}

message TradeRequest {
  ENUM_TRADE_REQUEST_ACTIONS action = 1;   // was: int32 action = 1
  // ... unchanged fields ...
  ENUM_ORDER_TYPE            type = 11;         // was: int32 type = 11
  ENUM_ORDER_TYPE_FILLING    type_filling = 12; // was: int32 type_filling = 12
  ENUM_ORDER_TYPE_TIME       type_time = 13;    // was: int32 type_time = 13
  // ... unchanged fields ...
}
```

### `order_calc.proto` — retype fields

```proto
message OrderCalcMarginRequest {
  ENUM_ORDER_TYPE action = 1;  // was: int32 action = 1
  // ... unchanged ...
}
message OrderCalcProfitRequest {
  ENUM_ORDER_TYPE action = 1;  // was: int32 action = 1
  // ... unchanged ...
}
```

All field numbers are preserved. proto3 enums encode as varint, identical to the
prior `int32` — no wire/data migration.

## Generated C# surface (namespace `Metatrader.V1`)

```csharp
public enum ENUM_ORDER_TYPE { ORDER_TYPE_BUY = 0, ORDER_TYPE_SELL = 1, /* ... */ }
// ENUM_TRADE_REQUEST_ACTIONS, ENUM_ORDER_TYPE_FILLING, ENUM_ORDER_TYPE_TIME likewise

public sealed partial class TradeRequest {
    public ENUM_TRADE_REQUEST_ACTIONS Action { get; set; }        // field 1
    public ENUM_ORDER_TYPE            Type { get; set; }          // field 11
    public ENUM_ORDER_TYPE_FILLING    TypeFilling { get; set; }   // field 12
    public ENUM_ORDER_TYPE_TIME       TypeTime { get; set; }      // field 13
}
public sealed partial class OrderCalcMarginRequest { public ENUM_ORDER_TYPE Action { get; set; } }
public sealed partial class OrderCalcProfitRequest { public ENUM_ORDER_TYPE Action { get; set; } }
```

The property names are the same as before (`Action`, `Type`, `TypeFilling`,
`TypeTime`); only their type changes from `int` to the enum.

## Behavioral guarantees (verifiable)

1. **Value fidelity** — `(int)member` equals the MT5 numeric value in
   [data-model.md](../data-model.md); the transmitted varint equals it.
   *(FR-003, SC-002)*
2. **Shared order-type identity** — `TradeRequest.Type`,
   `OrderCalcMarginRequest.Action`, `OrderCalcProfitRequest.Action` are all
   `ENUM_ORDER_TYPE`. *(FR-005, FR-015, SC-002)*
3. **Unknown-value round-trip** — for any `int n` (incl. no named member), setting
   the field from `(EnumType)n`, serializing, parsing, and reading yields `n`;
   never throws. *(FR-007, FR-008, SC-005)*
4. **Wire compatibility** — serialized bytes are identical to the prior `int32`
   contract for the same numeric value; the Python server reads identical values.
   *(FR-006, FR-013, SC-008)*
5. **Cross-field / raw-int type safety (compile-time)** — the following MUST NOT
   compile:

   ```csharp
   var req = new TradeRequest();
   req.Action = ENUM_ORDER_TYPE.ORDER_TYPE_BUY;   // CS0029 wrong enum type
   req.Action = 1;                                // CS0029 int is not the enum type
   ```
   *(FR-004, SC-003; see [research.md](../research.md) Decision 6)*
6. **Unset-action rejection** — a trade request with `Action` unset or
   `TRADE_ACTION_UNSPECIFIED` returns a structured error and places no order.
   *(FR-014, SC-009)*
7. **Target-framework parity** — all hold on netstandard2.0 / net48 and modern
   .NET. *(FR-011, SC-007)*

## Non-goals

- No response-side typing (deal/position/transaction) — request-only.
- No field renumber/rename; no new sentinel beyond `TRADE_ACTION_UNSPECIFIED=0`.
