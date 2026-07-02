# Quickstart: Named Values for MT5 Request Fields (C#)

The `MetaTrader.Grpc.Client` package exposes named, compile-checked values for the
request fields that MT5 treats as enums. You no longer need magic integers on
`action`, `type`, `type_filling`, `type_time`, or the calculation `action`.

The enum types use the verbatim MT5 names, so they line up 1:1 with the official
MQL5 documentation.

## Build a trade request with named values

```csharp
using MetaTrader.Grpc.Client;
using Metatrader.V1;

var request = new OrderSendRequest
{
    TradeRequest = new TradeRequest
    {
        Symbol      = "EURUSD",
        Volume      = 0.10,
        ActionEnum      = ENUM_TRADE_REQUEST_ACTIONS.TRADE_ACTION_DEAL,
        TypeEnum        = ENUM_ORDER_TYPE.ORDER_TYPE_BUY,
        TypeFillingEnum = ENUM_ORDER_TYPE_FILLING.ORDER_FILLING_IOC,
        TypeTimeEnum    = ENUM_ORDER_TYPE_TIME.ORDER_TIME_GTC,
    }
};

var result = await client.SendOrderAsync(request);
```

The same `TradeRequest` shape works for `CheckOrderAsync` via `OrderCheckRequest`.

## Calculation requests share the order-type set

```csharp
var margin = await client.CalcMarginAsync(new OrderCalcMarginRequest
{
    ActionEnum = ENUM_ORDER_TYPE.ORDER_TYPE_BUY,
    Symbol     = "EURUSD",
    Volume     = 0.10,
    Price      = 1.0850,
});

// Profit calc expects Buy or Sell (documented; not compile-enforced).
var profit = await client.CalcProfitAsync(new OrderCalcProfitRequest
{
    ActionEnum = ENUM_ORDER_TYPE.ORDER_TYPE_SELL,
    Symbol     = "EURUSD",
    Volume     = 0.10,
    PriceOpen  = 1.0850,
    PriceClose = 1.0800,
});
```

## The compiler catches wrong-field values

```csharp
var req = new TradeRequest();

// Does NOT compile — an order type is not a valid action:
// req.ActionEnum = ENUM_ORDER_TYPE.ORDER_TYPE_BUY;           // CS0029

// Does NOT compile — a time policy is not a valid filling policy:
// req.TypeFillingEnum = ENUM_ORDER_TYPE_TIME.ORDER_TIME_GTC; // CS0029
```

## Existing integer code keeps working

```csharp
// Unchanged, still compiles, still transmits the same values:
var legacy = new TradeRequest
{
    Symbol = "EURUSD",
    Volume = 0.10,
    Action = 1,        // TRADE_ACTION_DEAL
    Type   = 0,        // ORDER_TYPE_BUY
};

// The named and integer forms are equivalent:
// legacy.ActionEnum == ENUM_TRADE_REQUEST_ACTIONS.TRADE_ACTION_DEAL
```

## Sending a value MT5 adds in a future build

Named values do not lock you in. Any integer still round-trips:

```csharp
var req = new TradeRequest();
req.Action = 99;                    // some future MT5 value
int raw = (int)req.ActionEnum;      // == 99, no exception

req.ActionEnum = (ENUM_TRADE_REQUEST_ACTIONS)99;
int stillRaw = req.Action;          // == 99
```

## Verify it

From `mt5_grpc_client_csharp/`:

```bash
dotnet build src/MetaTrader.Grpc.Client/MetaTrader.Grpc.Client.csproj
dotnet test tests/MetaTrader.Grpc.Client.Tests/MetaTrader.Grpc.Client.Tests.csproj
dotnet test tests/MetaTrader.Grpc.Client.CompatibilityTests/MetaTrader.Grpc.Client.CompatibilityTests.csproj
```
