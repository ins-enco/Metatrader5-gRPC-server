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

---

# Migration Guide: `GetDealsAsync` → `GetAllDealsAsync` / `StreamDealsAsync` (5.1.1)

`GetDealsAsync` asks the server for the whole filtered deal history in **one**
message. On a large history that message is big enough to abort the server: 1571
deals measured at ~121 KB terminated it every time (the write fails inside grpcio's
Windows IOCP endpoint, exit `c0000409`) and a container with
`restart: unless-stopped` went into a restart loop. 982 deals (~75 KB) were never
seen to fail.

**Nothing about `GetDealsAsync` changed** — same signature, same behaviour, no
fallback added — so this migration is optional and can be done per call site. It is
worth doing for any call whose history can grow.

## Which replacement

| Your call site | Use | Why |
|---|---|---|
| Needs the full collection in memory (reconciliation, a report) | `GetAllDealsAsync` | One-token rename; identical result type |
| Processes deals as they arrive, or the history is large/unbounded | `StreamDealsAsync` | Holds one chunk at a time |

## `GetAllDealsAsync` — a one-token rename

The return type is **identical** to `GetDealsAsync`'s
(`Task<Mt5GrpcResult<DealsResponse>>`), so `IsSuccess`, `Error` and `Value.Deals`
all keep working and no response-handling code changes:

```csharp
// Before
var result = await client.GetDealsAsync(request);

// After — reads in bounded chunks underneath
var result = await client.GetAllDealsAsync(request);

if (result.IsSuccess) Console.WriteLine($"{result.Value!.Deals.Count} deals");
else Console.WriteLine($"{result.Error!.Operation}: {result.Error.Message}");
```

Two behaviours to know, neither of which breaks existing control flow:

- **Client memory is unbounded** — it retains every deal for the life of the call,
  exactly as `GetDealsAsync` did. This fixes the *server* failure, not the client
  cost. For a large history prefer `StreamDealsAsync`.
- **No partial collection on failure.** A failure returns `IsSuccess == false` with
  `Value` null, even when chunks arrived before the fault. `GetDealsAsync` had no
  partial state to expose, so this matches what callers already assume — and it
  means a truncated read can never be mistaken for a complete one.

## `StreamDealsAsync` — the chunk-level surface

```csharp
// Before
var result = await client.GetDealsAsync(request);
if (result.IsSuccess)
    foreach (var deal in result.Value!.Deals) Store(deal);

// After
await foreach (var chunk in client.StreamDealsAsync(request))
    foreach (var deal in chunk.Deals) Store(deal);
```

Note the error model differs here, and this is the one place a migrating caller
must change more than a name: `StreamDealsAsync` **throws**
`Mt5GrpcClientException` (with the mapped error on `.Error`) instead of returning a
failure result, because an `IAsyncEnumerable` has nowhere to put a result object.
Wrap the loop in `try`/`catch` where you previously checked `result.IsSuccess`. If
you would rather keep the returned-failure model, use `GetAllDealsAsync`.

## Server floor

Both new methods need a server at **`0.4.0` or later** — `StreamDeals` does not
exist before it. An older server fails the call with gRPC status `Unimplemented`,
and there is **no automatic fallback** to `GetDeals`: falling back would send the
oversized single response that terminates the server, turning a clear error into an
outage. `GetDealsAsync` continues to work against any supported server.

Chunk size is server policy: `chunk_size` unset means 500 deals per message and a
larger value is capped at 1000. The client transmits your value verbatim. Prefer
leaving it unset — a chunk at the 1000 cap is ~77 KB, back inside the band where
aborts were observed.
