# MetaTrader.Grpc.Client

`MetaTrader.Grpc.Client` is a `netstandard2.0` C# client package generated from
the repository's `protos/*.proto` contracts. It exposes the generated gRPC
clients for advanced callers and a thin wrapper that returns typed
`Mt5GrpcResult<T>` values for convenience calls.

Package metadata uses independent client SemVer. The current package version is
`5.1.1`, with proto contract identity `protos-007-stream-deals` and a
tested server range of `[0.4.0,1.0.0)`.

> **5.1.1 (additive)**: adds `StreamDealsAsync` and `GetAllDealsAsync` over the new
> server-streaming `TradeHistoryService.StreamDeals` RPC, so a large deal history
> can be read without the single oversized response that terminates the server.
> See [Deal history](#deal-history). `GetDealsAsync` is unchanged apart from
> documentation, and no dependency or target framework changed. **Requires a server
> at `0.4.0` or later** — an older server fails the call as `Unimplemented`, with no
> automatic fallback.

> **5.0.0 (additive)**: adds six intent-focused trade lifecycle methods for
> opening, ticket-only position closing and pending-order cancellation,
> modifying, single close-by, and automatic multiple close-by.
> The wire contract, generated bindings, server, and generic `SendOrderAsync`
> behavior are unchanged.

> **0.3.0 (additive)**: adds `TradeEventsService.SubscribeTradeTransactions`, the
> first server-streaming RPC. See [Trade transaction events](#trade-transaction-events).
> No existing RPC, message, field, or field number changed — fully backward compatible.

> **0.2.0 is a breaking change**: request fields that MT5 treats as enums are now
> native enum types, so raw-integer assignments no longer compile. See
> [Request enum fields](#request-enum-fields) and [MIGRATION.md](./MIGRATION.md).

## Install from GitHub Packages

The package is published to the organization's GitHub Packages NuGet registry at
`https://nuget.pkg.github.com/ins-enco/index.json`. Consuming it is the supported
way to use the client from another project — you do **not** need a checkout of this
repository, and no protobuf/gRPC code generation runs in your project.

### 1. Authenticate

GitHub Packages requires authentication for NuGet restore. Create a GitHub
Personal Access Token (classic) with at least the **`read:packages`** scope and
expose it (plus your GitHub username) to your shell:

```powershell
$env:GITHUB_ACTOR = "your-github-username"
$env:GITHUB_PACKAGES_TOKEN = "ghp_xxx"   # PAT with read:packages
```

### 2. Add the feed source

Add a `nuget.config` next to your solution (see
[`examples/nuget.config`](./examples/nuget.config) for a ready-to-copy file). No
token is committed — the credentials are read from the environment:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="github-ins-enco" value="https://nuget.pkg.github.com/ins-enco/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <github-ins-enco>
      <add key="Username" value="%GITHUB_ACTOR%" />
      <add key="ClearTextPassword" value="%GITHUB_PACKAGES_TOKEN%" />
    </github-ins-enco>
  </packageSourceCredentials>
</configuration>
```

`nuget.org` is included so the client's runtime dependencies resolve automatically.

### 3. Add the single reference

```xml
<PackageReference Include="MetaTrader.Grpc.Client" Version="5.0.0" />
```

Restore resolves the package **and** all of its runtime dependencies
(Google.Protobuf, Grpc.Core.Api, Grpc.Net.Client, Microsoft.Bcl.AsyncInterfaces,
Microsoft.Extensions.Logging.Abstractions) — you add no other packages by hand,
and `Grpc.Tools` never enters your project. Then use the client as shown in
[Wrapper Results](#wrapper-results).

### Stable vs. pre-release versions

Production versions use plain SemVer (`5.1.1`). Pre-release builds carry a SemVer
pre-release suffix (for example `5.1.1-preview.1`). NuGet **excludes pre-release
versions by default**, so a normal restore only picks stable versions; opt in
explicitly (e.g. `dotnet add package MetaTrader.Grpc.Client --prerelease`, or a
floating `5.1.1-*` version) to consume a pre-release.

### If restore fails

- **401 / authentication** — the token is missing or lacks `read:packages`.
  Re-check step 1. This is a clear, expected failure, **not** a partial or
  silently broken restore.
- **Network / offline** — the feed is unreachable; restore fails cleanly rather
  than producing a half-working client. Reconnect and retry.
- **Unsupported target framework** — a consumer targeting a framework outside the
  supported set fails restore/build clearly rather than producing a subtly
  non-working client.

.NET Framework 4.8 consumers use the package's `net472` asset and native
`Grpc.Core` channel as described below.

### .NET Framework 4.8 transport

Use `Mt5GrpcClientFactory.CreateCore` on .NET Framework 4.8. The package's
`net472` dependency group carries the legacy native `Grpc.Core` transport, so
the consumer does not add `WinHttpHandler` directly. See
`examples/NetFramework48ClientExample` for a complete example.

## Build

```powershell
dotnet restore mt5_grpc_client_csharp/MetaTrader.Grpc.Client.sln
dotnet build mt5_grpc_client_csharp/MetaTrader.Grpc.Client.sln -c Release
dotnet test mt5_grpc_client_csharp/MetaTrader.Grpc.Client.sln -c Release
dotnet pack mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/MetaTrader.Grpc.Client.csproj -c Release
```

## Generated Clients

```csharp
using Grpc.Net.Client;
using Metatrader.V1;

using var channel = GrpcChannel.ForAddress("http://localhost:50051");
var accountClient = new AccountInfoService.AccountInfoServiceClient(channel);
var account = await accountClient.GetAccountInfoAsync(new AccountInfoRequest());
```

The generated namespace comes from the current proto package and is
`Metatrader.V1`. Generated clients preserve protobuf binary communication,
optional field presence, repeated value ordering, timestamps, 64-bit identifiers,
and numeric market values.

## Request enum fields

As of `0.2.0`, the request fields MT5 treats as enumerations are **native enum
types** (in `Metatrader.V1`), so the compiler restricts each field to its valid
option set and the editor lists the choices:

```csharp
using Metatrader.V1;

var request = new OrderSendRequest
{
    TradeRequest = new TradeRequest
    {
        Symbol      = "EURUSD",
        Volume      = 0.10,
        Action      = ENUM_TRADE_REQUEST_ACTIONS.TradeActionDeal,
        Type        = ENUM_ORDER_TYPE.OrderTypeBuy,
        TypeFilling = ENUM_ORDER_TYPE_FILLING.OrderFillingIoc,
        TypeTime    = ENUM_ORDER_TYPE_TIME.OrderTimeGtc,
    }
};
```

The margin and profit calculation requests share the same `ENUM_ORDER_TYPE`:

```csharp
var margin = await client.CalcMarginAsync(new OrderCalcMarginRequest
{
    Action = ENUM_ORDER_TYPE.OrderTypeBuy, Symbol = "EURUSD", Volume = 0.10, Price = 1.0850,
});
```

The protobuf compiler renders MT5's `TRADE_ACTION_DEAL`-style names in PascalCase
(`TradeActionDeal`); the wire name is preserved for cross-referencing MQL5 docs.
Leaving `Action` unset is `TradeActionUnspecified` (0), which the server rejects
with a structured error — always set an explicit action. Upgrading from `0.1.x`?
See [MIGRATION.md](./MIGRATION.md).

## Wrapper Results

```csharp
using MetaTrader.Grpc.Client;
using Metatrader.V1;

var options = new Mt5GrpcClientOptions
{
    Address = new Uri("http://localhost:50051"),
    DefaultDeadline = TimeSpan.FromSeconds(5)
};

using var client = Mt5GrpcClientFactory.Create(options);
var result = await client.GetAccountInfoAsync(deadline: DateTime.UtcNow.AddSeconds(2));

if (!result.IsSuccess)
{
    Console.WriteLine($"{result.Error!.Operation}: {result.Error.Message}");
    return;
}

Console.WriteLine(result.Value!.AccountInfo.Login);
```

Wrapper methods do not impose a built-in timeout. A client-wide
`DefaultDeadline` is optional, and a per-call deadline overrides it. Cancellation
tokens are forwarded to the generated gRPC call.

## Trade lifecycle operations

Version 5.0.0 adds six intent-focused methods. They build a fresh
`TradeRequest`, choose the MT5 action, perform local structural validation, and
delegate to the unchanged `SendOrderAsync` path:

- `OpenOrderAsync` opens a market BUY/SELL or places a pending order.
- `ClosePositionAsync` requires only a position ticket and an optional volume.
  It retrieves the position and symbol execution settings, derives the opposite
  DEAL fields internally, and shares one effective deadline across both lookups
  and the send. Omit volume for a full close; supply a positive volume no greater
  than the current position volume for a partial close.
- `CloseOrderAsync` cancels a pending order from its pending-order ticket by
  mapping it to `TRADE_ACTION_REMOVE`.
- `ModifyTradeAsync` maps a `PositionModification` to SLTP or a complete final
  `PendingOrderModification` state to MODIFY. Zero SL/TP explicitly clears that
  value when MT5 permits it.
- `ClosePositionByAsync` preserves the two caller-supplied ticket roles. Close-by
  requires compatible opposite positions on a hedging account; MT5 remains
  authoritative for live account and symbol rules.
- `ClosePositionsByAsync` discovers one symbol and optional magic scope, freezes
  its initial tickets, refreshes that membership, and submits FIFO close-by pairs
  sequentially.

Every single-operation result separates the gRPC/shared-error outcome from the
MT5 execution outcome:

```csharp
var open = await client.OpenOrderAsync(new OpenOrderRequest(
    "EURUSD", ENUM_ORDER_TYPE.OrderTypeBuy, 0.10)
{
    FillingPolicy = ENUM_ORDER_TYPE_FILLING.OrderFillingIoc,
    TimePolicy = ENUM_ORDER_TYPE_TIME.OrderTimeGtc,
    StopLoss = 1.0750,
    TakeProfit = 1.0950
});

if (!open.CallResult.IsSuccess)
{
    Console.WriteLine(open.CallResult.Error!.Message);
}
else
{
    Console.WriteLine($"execution={open.ExecutionStatus}, retcode={open.RawRetcode}");
}
```

`CallResult.IsSuccess` only means an order-send response was received without a
shared MT5 error payload. Inspect `ExecutionStatus` and `RawRetcode` before
considering a trade completed. `AcceptedOrPlaced`, `Unknown`, cancellation, and
transport failures can be execution-uncertain; do not retry them automatically.

Position and pending-order closing require only ticket-oriented calls:

```csharp
var fullClose = await client.ClosePositionAsync(positionTicket: 1001);
var partialClose = await client.ClosePositionAsync(positionTicket: 1002, volume: 0.05);
var cancelledOrder = await client.CloseOrderAsync(orderTicket: 2001);
```

Invalid tickets or volumes perform no RPC. A valid position close performs one
ticket-filtered position lookup and one symbol-info lookup before at most one
order-send RPC. A valid pending-order cancellation performs exactly one
order-send RPC. Neither operation retries automatically.

Batch close-by is sequential and non-atomic: no rollback is attempted, and a
later failure does not reverse an earlier successful pair. Inspect every pair,
plus the deterministic remainder list:

```csharp
var batch = await client.ClosePositionsByAsync(new ClosePositionsByRequest("EURUSD")
{
    Magic = 42,
    Comment = "strategy-close-by"
});

foreach (var pair in batch.Pairs)
{
    Console.WriteLine($"{pair.PairIndex}: {pair.PositionTicket}/{pair.OppositePositionTicket} {pair.AttemptState}");
    if (pair.OperationResult != null)
    {
        Console.WriteLine($"call={pair.OperationResult.CallResult.IsSuccess}, execution={pair.OperationResult.ExecutionStatus}");
    }
}

foreach (var remainder in batch.Remainders)
{
    Console.WriteLine($"remainder {remainder.Ticket}: {remainder.Reason}");
}
```

The runnable NetStandard and .NET Framework examples contain market/pending
open, full/partial position close, pending-order cancellation,
position/pending modification, single close-by, and batch inspection. Because
they submit real trades, opt in with
`RUN_TRADE_LIFECYCLE_EXAMPLES=1` on a test account after reviewing tickets and
prices.

## Security

Plaintext endpoints are allowed by default when no TLS options are supplied:

```csharp
var local = new Mt5GrpcClientOptions { Address = new Uri("http://localhost:50051") };
```

TLS is used when TLS options are supplied. If an `http://` address is combined
with TLS options, the factory resolves the channel address to `https://`.

```csharp
var remote = new Mt5GrpcClientOptions
{
    Address = new Uri("https://mt5-grpc.example.com:50051"),
    TlsOptions = Mt5GrpcTlsOptions.SystemTrust()
};
```

## Logging

Set `LoggerFactory` to observe channel creation, transport and gRPC failures,
deadline or cancellation outcomes, and MT5 error payloads. Logs avoid raw
payload dumps and credentials.

## Examples

The `examples/NetStandardClientExample` project demonstrates account, symbol,
market data, order validation, order submission, lifecycle operations, and typed
error handling. The `examples/NetFramework48ClientExample` project demonstrates
.NET Framework 4.8 usage with the native `Grpc.Core` channel and the same
lifecycle surface.

Expected output for the live examples is either the requested account login or a
typed failure line in the form `Service.Method: failure message`.

## Performance

The package preserves gRPC protobuf binary communication and does not require
callers to serialize or parse text payloads. Benchmark validation compares direct
generated-client shapes with wrapper result mapping:

```powershell
dotnet run -c Release --project mt5_grpc_client_csharp/benchmarks/MetaTrader.Grpc.Client.Benchmarks/MetaTrader.Grpc.Client.Benchmarks.csproj
```

Representative unary workflows should stay within 10% overhead versus direct
generated clients in the same environment. The unit performance budget test
keeps wrapper result mapping bounded, and full benchmark numbers should be used
for release decisions.

## Trade transaction events

`TradeEventsService.SubscribeTradeTransactions` (added in `0.3.0`) is the first
server-streaming RPC. It emits one `TradeTransactionEvent` per newly added deal on
the connected account as it is observed — exactly once, in chronological order,
with no duplicates. Delivery is emulated by server-side polling of the MT5 deals
history (there is no push callback in the MT5 Python API), so "real-time" means
"within one poll interval" (default 1000 ms; server floor 200 ms). A subscription
starts at "now" by default; supply `FromTimeMsc` to backfill from a past point
(capped to a 7-day lookback).

**Primary surface — `IAsyncEnumerable<TradeTransactionEvent>` (1:1 with the stream):**

```csharp
using var client = Mt5GrpcClientFactory.Create("https://localhost:50051");
using var cts = new CancellationTokenSource();

var request = new SubscribeTradeTransactionsRequest(); // start now, default cadence
await foreach (var evt in client.SubscribeTradeTransactionsAsync(request, cancellationToken: cts.Token))
{
    Console.WriteLine($"deal {evt.DealTicket} {evt.Symbol} vol={evt.Volume} @ {evt.Price}");
}
// A terminal MT5 failure throws Mt5GrpcClientException whose .Error carries the mapped error.
```

**Convenience surface — C# `event` wrapper (`TradeTransactionSubscription`):**

```csharp
var subscription = client.SubscribeTradeTransactions(new SubscribeTradeTransactionsRequest());
subscription.TransactionReceived += (_, evt) => Console.WriteLine($"deal {evt.DealTicket}");
subscription.Faulted += (_, error) => Console.WriteLine($"faulted: {error.Message}"); // resubscribe from last time
subscription.Completed += (_, _) => Console.WriteLine("stream ended");
subscription.Start();
// ...
subscription.Stop();   // graceful cancellation; releases the server-side worker
```

On disconnect, resume by starting a new subscription with `FromTimeMsc` set to the
last received `TimeMsc`; the boundary deal is de-duplicated so there is no gap and
no duplicate.

.NET Framework 4.8 consumers can reference the `netstandard2.0` package, but
gRPC uses the package's native `Grpc.Core` transport. Server-streaming support
still depends on the Windows host and is not guaranteed on all .NET Framework deployments.

## Deal history

Reading the closed deal history has two surfaces. `TradeHistoryService.StreamDeals`
(added in server `0.4.0`) delivers the filtered history as a stream of bounded
chunks, and the client exposes it two ways:

| Method | Returns | Client memory |
| --- | --- | --- |
| `StreamDealsAsync` | `IAsyncEnumerable<DealsResponse>` — one item per server message | one chunk at a time |
| `GetAllDealsAsync` | `Task<Mt5GrpcResult<DealsResponse>>` — the ordered concatenation | the whole history |
| `GetDealsAsync` | `Task<Mt5GrpcResult<DealsResponse>>` — one single response | the whole history |

**`GetDealsAsync` has a large-history failure mode.** It asks the server for the
entire history in one message. Measured against a live account, 1571 deals
serialise to about 121 KB, and a response that size **terminates the server**: the
write aborts inside grpcio's Windows IOCP endpoint (`windows_endpoint.cc`, exit
`c0000409`) and a container with `restart: unless-stopped` goes into a restart
loop. 982 deals (~75 KB) were never seen to fail. `GetDealsAsync` is unchanged and
still fine for a small or tightly filtered history, but **for anything large use
`StreamDealsAsync`** — or `GetAllDealsAsync`, which reads in chunks underneath and
hands back the same result type.

### Backfill once, then fetch incrementally

The closed history only ever grows at the newest end, so re-reading all of it every
cycle is wasted transfer — and the habit that triggers the failure above. Read it
once, keep the newest `time_msc` you hold as an **anchor**, then fetch only what is
new by setting `TimeFilter.date_from` to that anchor. With no new activity an
incremental cycle transfers zero deals.

```csharp
using var client = Mt5GrpcClientFactory.Create("https://localhost:50051");

// date_to is a real upper bound, not "no limit". The server passes both values
// straight to MT5, so DateTo = 0 means 1970-01-01 and a 0..0 window matches
// nothing -- the most common cause of an unexpectedly empty read. MT5 stamps
// deals in broker server time, which can run hours ahead of UTC, so leave a
// margin rather than using "now" exactly, and recompute it each polling cycle.
var upperBound = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds();

// 1. Backfill: the whole history, one bounded chunk at a time.
var backfill = new DealsRequest { TimeFilter = new TimeFilter { DateFrom = 0, DateTo = upperBound } };
long anchorMsc = 0;

await foreach (var chunk in client.StreamDealsAsync(backfill))
{
    foreach (var deal in chunk.Deals)
    {
        Store(deal);
        if (deal.TimeMsc > anchorMsc) anchorMsc = deal.TimeMsc;
    }
}

// 2. Incremental: only what arrived after the anchor. date_from is in seconds
// and is inclusive, so the anchor deal may come back — de-duplicate on Ticket.
var incremental = new DealsRequest
{
    TimeFilter = new TimeFilter { DateFrom = anchorMsc / 1000, DateTo = upperBound },
};

await foreach (var chunk in client.StreamDealsAsync(incremental))
{
    foreach (var deal in chunk.Deals)
    {
        if (deal.TimeMsc > anchorMsc) { Store(deal); anchorMsc = deal.TimeMsc; }
    }
}
```

The library holds no anchor for you and offers no helper — the anchor lives in your
own state. Zero chunks is a valid, successful, empty read, never an error.

### One call when memory allows

`GetAllDealsAsync` is a one-token rename away from `GetDealsAsync`: identical
return type, so `IsSuccess`, `Error` and `Value.Deals` keep working.

```csharp
var result = await client.GetAllDealsAsync(request);
if (result.IsSuccess) Console.WriteLine($"{result.Value!.Deals.Count} deals");
else Console.WriteLine($"{result.Error!.Operation}: {result.Error.Message}");
```

The trade-off is **unbounded client memory**: it retains every deal for the life of
the call, so cost grows with the history instead of staying flat. Prefer
`StreamDealsAsync` for a large history. Failures are returned rather than thrown —
an in-band MT5 error, a transport fault, cancellation or a deadline all come back as
`IsSuccess == false` with `Value` null — and **no partial collection is ever
returned**, so a truncated read can never be mistaken for a complete one. If you
want to keep partial progress, use `StreamDealsAsync`.

### Chunk size is the server's policy

`DealsRequest.chunk_size` (field 5, `StreamDeals` only — `GetDeals` ignores it) sets
deals per streamed message. **The server owns the policy**: unset means its default
of **500**, and any larger value is clamped to its cap of **1000**. The client
transmits your value verbatim and supplies none of its own — it never sets, raises,
lowers, clamps, defaults or clears the field, so leaving it unset is what gets you
the server's default.

> **Prefer the default.** At roughly 77 bytes per deal, a chunk at the 1000 cap is
> about **77 KB** — back inside the band where aborts were observed (~75 KB
> survived, ~121 KB did not). Requesting a size at or near the cap gives back the
> very failure mode this surface exists to avoid.

### Server version

`StreamDeals` requires a server at `0.4.0` or later. An older server fails the call
with gRPC status `Unimplemented`, and there is **no automatic fallback** to
`GetDeals` in either direction — a fallback would send exactly the oversized single
response that terminates the server, turning a clear, actionable error into an
outage. Check the tested server range in this README's header before deploying.

Both runnable [examples](#examples) demonstrate the full pattern, the `net48` one
over the native `Grpc.Core` channel via `Mt5GrpcClientFactory.CreateCore`.

## Publishing a new version (maintainers)

Publishing is automated and tag-triggered — there is no manual publish step and no
publish credential on a maintainer's machine (credentials live only in CI as the
built-in `GITHUB_TOKEN`).

1. **Bump `<Version>`** in
   [MetaTrader.Grpc.Client.csproj](./src/MetaTrader.Grpc.Client/MetaTrader.Grpc.Client.csproj).
   For a breaking change also update `<ProtoContractIdentity>` /
   `<TestedServerVersionRange>`, this README, `CHANGELOG.md`, `MIGRATION.md`, and
   `<PackageReleaseNotes>` (which must quote the current contract identity and
   server range — the drift-guard test enforces this).
2. **Verify locally**:

   ```powershell
   dotnet restore mt5_grpc_client_csharp/MetaTrader.Grpc.Client.sln
   dotnet build   mt5_grpc_client_csharp/MetaTrader.Grpc.Client.sln -c Release
   dotnet test    mt5_grpc_client_csharp/MetaTrader.Grpc.Client.sln -c Release
   mt5_grpc_client_csharp/scripts/check-generated.ps1        -Configuration Release
   mt5_grpc_client_csharp/scripts/check-package-metadata.ps1 -Configuration Release
   mt5_grpc_client_csharp/scripts/verify-consumer-restore.ps1 -Configuration Release
   ```

3. **Tag and push** — the tag version must equal `<Version>`:

   ```powershell
   git tag v5.1.1
   git push origin v5.1.1
   ```

The [`csharp-client-publish`](../.github/workflows/csharp-client-publish.yml)
workflow (tags `v*.*.*`) then builds, tests, runs the drift and metadata
gates, checks the tag matches `<Version>`, packs deterministically
(`ContinuousIntegrationBuild=true`), and pushes to GitHub Packages with
`GITHUB_TOKEN`. Publishing a version that already exists is rejected (HTTP 409) and
fails the job — published versions are immutable; ship a correction as a new
version.

> **The `v*.*.*` tag namespace is shared with the server's Docker release**
> ([`docker-ghcr.yml`](../.github/workflows/docker-ghcr.yml)): one `v<X.Y.Z>` tag
> fires both workflows. The two artifacts version independently — the server and
> proto packages are on `0.x`, this client on `5.x` — so a tag matches only one of
> them at a time:
>
> - `v0.4.0` (a server/proto release) also starts this workflow, whose tag guard
>   compares `0.4.0` against `<Version>` `5.1.1` and fails the job. Nothing is
>   published, and the failed run is expected rather than a problem to fix.
> - `v5.1.1` (a client release) also starts the Docker release, which pushes
>   `mt5-grpc-server:5.1.1` — a server image numbered after the client. This has
>   already happened once: `mt5-grpc-server:5.1.0` exists and holds a `0.4.0` server.
>
> Check which artifact a tag is for before pushing it.

## Drift Check

```powershell
mt5_grpc_client_csharp/scripts/check-generated.ps1 -Configuration Release
```

The script builds the package from `protos/*.proto` and fails if generated C#
bindings cannot be regenerated and compiled.
