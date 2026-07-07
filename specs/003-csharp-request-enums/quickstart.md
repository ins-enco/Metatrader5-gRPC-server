# Quickstart: Named Values for MT5 Request Fields (C#)

As of `0.2.0`, the request fields that MT5 treats as enums are **native enum types**
in the contract. `action`, `type`, `type_filling`, `type_time`, and the calculation
`action` are no longer `int` — the compiler now guides you to the right values.

> **Naming**: the protobuf C# generator renders MT5's `TRADE_ACTION_DEAL`-style
> member names in PascalCase (`TradeActionDeal`); the verbatim MT5 wire name is
> preserved as metadata so you can still cross-reference the MQL5 docs. Use the
> PascalCase form in C# code (shown below).

> **Breaking change**: code that assigned raw integers to these fields (e.g.
> `Action = 1`) no longer compiles. See [Migrating from integers](#migrating-from-integers).

## Build a trade request with named values

```csharp
using Metatrader.V1;

var request = new OrderSendRequest
{
    TradeRequest = new TradeRequest
    {
        Symbol       = "EURUSD",
        Volume       = 0.10,
        Action       = ENUM_TRADE_REQUEST_ACTIONS.TradeActionDeal,
        Type         = ENUM_ORDER_TYPE.OrderTypeBuy,
        TypeFilling  = ENUM_ORDER_TYPE_FILLING.OrderFillingIoc,
        TypeTime     = ENUM_ORDER_TYPE_TIME.OrderTimeGtc,
    }
};

var result = await client.SendOrderAsync(request);
```

The same `TradeRequest` works for `CheckOrderAsync` via `OrderCheckRequest`.

## Calculation requests share the order-type set

```csharp
var margin = await client.CalcMarginAsync(new OrderCalcMarginRequest
{
    Action = ENUM_ORDER_TYPE.OrderTypeBuy,
    Symbol = "EURUSD",
    Volume = 0.10,
    Price  = 1.0850,
});

// Profit calc expects Buy or Sell (documented; not compile-enforced).
var profit = await client.CalcProfitAsync(new OrderCalcProfitRequest
{
    Action     = ENUM_ORDER_TYPE.OrderTypeSell,
    Symbol     = "EURUSD",
    Volume     = 0.10,
    PriceOpen  = 1.0850,
    PriceClose = 1.0800,
});
```

## The compiler catches wrong-field and raw-int values

```csharp
var req = new TradeRequest();

// Does NOT compile — an order type is not a valid action:
// req.Action = ENUM_ORDER_TYPE.OrderTypeBuy;                 // CS0029

// Does NOT compile — a raw integer is not the enum type:
// req.Action = 1;                                            // CS0029
```

## Migrating from integers

Replace each integer with its named value (identical transmitted value):

```csharp
// Before (0.1.x):
var t = new TradeRequest { Action = 1, Type = 0, TypeFilling = 1, TypeTime = 0 };

// After (0.2.0):
var t = new TradeRequest
{
    Action      = ENUM_TRADE_REQUEST_ACTIONS.TradeActionDeal,   // 1
    Type        = ENUM_ORDER_TYPE.OrderTypeBuy,                 // 0
    TypeFilling = ENUM_ORDER_TYPE_FILLING.OrderFillingIoc,      // 1
    TypeTime    = ENUM_ORDER_TYPE_TIME.OrderTimeGtc,            // 0
};
```

For a numeric value MT5 may add in a future build that has no named member, cast
explicitly — proto3 open enums preserve it losslessly:

```csharp
var req = new TradeRequest { Action = (ENUM_TRADE_REQUEST_ACTIONS)99 };
int raw = (int)req.Action;   // == 99, round-trips through serialization, no throw
```

## Note on the default action

Leaving `Action` unset is `TRADE_ACTION_UNSPECIFIED` (0), which MT5 does not
define. The server rejects it with a structured error and places no order — always
set an explicit action.

## Verify it

Regenerate + test from repo scripts:

```bash
# C# client (regenerates bindings on build)
dotnet build mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/MetaTrader.Grpc.Client.csproj
dotnet test  mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.Tests/MetaTrader.Grpc.Client.Tests.csproj
dotnet test  mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.CompatibilityTests/MetaTrader.Grpc.Client.CompatibilityTests.csproj

# Python bindings + server (regenerate mt5_grpc_proto, then run server tests)
# (use the repository's documented proto-generation and pytest commands)
```
