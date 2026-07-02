# Contract: C# Request Enum Public API

This library exposes no new gRPC service surface. Its contract is the **C# public
API** it adds to the `MetaTrader.Grpc.Client` package. The `.proto` files and the
generated wire types are unchanged; this document specifies the C#-visible
additions and the guarantees they must uphold.

## Namespace and types

Namespace: `MetaTrader.Grpc.Client`

```csharp
namespace MetaTrader.Grpc.Client
{
    public enum ENUM_TRADE_REQUEST_ACTIONS
    {
        TRADE_ACTION_DEAL     = 1,
        TRADE_ACTION_PENDING  = 5,
        TRADE_ACTION_SLTP     = 6,
        TRADE_ACTION_MODIFY   = 7,
        TRADE_ACTION_REMOVE   = 8,
        TRADE_ACTION_CLOSE_BY = 10,
    }

    public enum ENUM_ORDER_TYPE
    {
        ORDER_TYPE_BUY             = 0,
        ORDER_TYPE_SELL            = 1,
        ORDER_TYPE_BUY_LIMIT       = 2,
        ORDER_TYPE_SELL_LIMIT      = 3,
        ORDER_TYPE_BUY_STOP        = 4,
        ORDER_TYPE_SELL_STOP       = 5,
        ORDER_TYPE_BUY_STOP_LIMIT  = 6,
        ORDER_TYPE_SELL_STOP_LIMIT = 7,
        ORDER_TYPE_CLOSE_BY        = 8,
    }

    public enum ENUM_ORDER_TYPE_FILLING
    {
        ORDER_FILLING_FOK    = 0,
        ORDER_FILLING_IOC    = 1,
        ORDER_FILLING_RETURN = 2,
    }

    public enum ENUM_ORDER_TYPE_TIME
    {
        ORDER_TIME_GTC           = 0,
        ORDER_TIME_DAY           = 1,
        ORDER_TIME_SPECIFIED     = 2,
        ORDER_TIME_SPECIFIED_DAY = 3,
    }
}
```

## Companion accessor properties

Declared on `partial class` extensions of the generated messages in namespace
`Metatrader.V1`. Raw `int` properties are unchanged and remain available.

```csharp
namespace Metatrader.V1
{
    public sealed partial class TradeRequest
    {
        public ENUM_TRADE_REQUEST_ACTIONS ActionEnum { get; set; }   // over int Action (field 1)
        public ENUM_ORDER_TYPE            TypeEnum { get; set; }     // over int Type (field 11)
        public ENUM_ORDER_TYPE_FILLING    TypeFillingEnum { get; set; } // over int TypeFilling (field 12)
        public ENUM_ORDER_TYPE_TIME       TypeTimeEnum { get; set; } // over int TypeTime (field 13)
    }

    public sealed partial class OrderCalcMarginRequest
    {
        public ENUM_ORDER_TYPE ActionEnum { get; set; }             // over int Action (field 1)
    }

    public sealed partial class OrderCalcProfitRequest
    {
        public ENUM_ORDER_TYPE ActionEnum { get; set; }             // over int Action (field 1)
    }
}
```

## Behavioral guarantees (verifiable)

1. **Value fidelity** — For every named member, `(int)member` equals the MT5
   numeric value in [data-model.md](../data-model.md). Setting `XxxEnum = member`
   makes the raw `Xxx` property (and therefore the transmitted `int32`) equal that
   value. *(FR-003, SC-002)*

2. **Shared order-type mapping** — `TradeRequest.TypeEnum`,
   `OrderCalcMarginRequest.ActionEnum`, and `OrderCalcProfitRequest.ActionEnum`
   all use `ENUM_ORDER_TYPE` with identical numeric mapping. *(FR-005, SC-002)*

3. **Backward compatibility** — The raw `int` properties (`Action`, `Type`,
   `TypeFilling`, `TypeTime`) are unchanged. Existing code that sets them with
   integers compiles unmodified and transmits identical values. Mixing raw-int and
   companion assignment on the same request produces correct per-field values.
   *(FR-006, SC-004)*

4. **Undefined-value round-trip** — For any `int n` (including values with no
   named member): `req.Action = n; ((int)req.ActionEnum) == n`, and
   `req.ActionEnum = (ENUM_TRADE_REQUEST_ACTIONS)n; req.Action == n`. Neither
   direction throws or alters the value. *(FR-007, FR-008, SC-005)*

5. **Cross-field type safety (compile-time)** — The following MUST NOT compile:

   ```csharp
   var req = new TradeRequest();
   req.ActionEnum = ENUM_ORDER_TYPE.ORDER_TYPE_BUY;           // CS0029: wrong enum type
   req.TypeFillingEnum = ENUM_ORDER_TYPE_TIME.ORDER_TIME_GTC; // CS0029: wrong enum type
   ```

   Verified by documented does-not-compile snippets (see
   [research.md](../research.md) Decision 4). *(FR-004, SC-003)*

6. **Target-framework parity** — All of the above hold identically on
   `netstandard2.0` / .NET Framework 4.8 and on modern .NET. *(FR-011, SC-007)*

## Non-goals (unchanged surface)

- No `.proto` change; wire type stays `int32`; no field renumber/rename.
- No response-side typing (deal/position/transaction types) — request-only.
- No sentinel values, no validation that rejects unknown integers.
