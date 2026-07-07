# MetaTrader.Grpc.Client

`MetaTrader.Grpc.Client` is a `netstandard2.0` C# client package generated from
the repository's `protos/*.proto` contracts. It exposes the generated gRPC
clients for advanced callers and a thin wrapper that returns typed
`Mt5GrpcResult<T>` values for convenience calls.

Package metadata uses independent client SemVer. The current package version is
`0.3.0`, with proto contract identity `protos-005-trade-transaction-events` and a
tested server range of `[0.3.0,1.0.0)`.

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
<PackageReference Include="MetaTrader.Grpc.Client" Version="0.2.0" />
```

Restore resolves the package **and** all of its runtime dependencies
(Google.Protobuf, Grpc.Core.Api, Grpc.Net.Client, Microsoft.Bcl.AsyncInterfaces,
Microsoft.Extensions.Logging.Abstractions) — you add no other packages by hand,
and `Grpc.Tools` never enters your project. Then use the client as shown in
[Wrapper Results](#wrapper-results).

### Stable vs. pre-release versions

Production versions use plain SemVer (`0.2.0`). Pre-release builds carry a SemVer
pre-release suffix (for example `0.3.0-preview.1`). NuGet **excludes pre-release
versions by default**, so a normal restore only picks stable versions; opt in
explicitly (e.g. `dotnet add package MetaTrader.Grpc.Client --prerelease`, or a
floating `0.3.0-*` version) to consume a pre-release.

### If restore fails

- **401 / authentication** — the token is missing or lacks `read:packages`.
  Re-check step 1. This is a clear, expected failure, **not** a partial or
  silently broken restore.
- **Network / offline** — the feed is unreachable; restore fails cleanly rather
  than producing a half-working client. Reconnect and retry.
- **Unsupported target framework** — a consumer targeting a framework outside the
  supported set fails restore/build clearly rather than producing a subtly
  non-working client.

.NET Framework 4.8 consumers reference the same `netstandard2.0` package but must
satisfy the transport prerequisite below.

### .NET Framework 4.8 transport prerequisite

gRPC-over-HTTP/2 on net48 requires TLS and `System.Net.Http.WinHttpHandler` on the
channel. See the `examples/NetFramework48ClientExample` project for the exact
`WinHttpHandler` setup.

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
market data, order validation, order submission, and typed error handling. The
`examples/NetFramework48ClientExample` project demonstrates .NET Framework 4.8
usage with TLS and `WinHttpHandler`.

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
gRPC over HTTP/2 requires TLS and `WinHttpHandler`. Server-streaming support
depends on the Windows host and is not guaranteed on all .NET Framework deployments.

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
   git tag csharp-client-v0.2.0
   git push origin csharp-client-v0.2.0
   ```

The client-scoped [`csharp-client-publish`](../.github/workflows/csharp-client-publish.yml)
workflow (tags `csharp-client-v*`) then builds, tests, runs the drift and metadata
gates, checks the tag matches `<Version>`, packs deterministically
(`ContinuousIntegrationBuild=true`), and pushes to GitHub Packages with
`GITHUB_TOKEN`. Publishing a version that already exists is rejected (HTTP 409) and
fails the job — published versions are immutable; ship a correction as a new
version. This client tag is independent of the server's `v*.*.*` Docker release.

## Drift Check

```powershell
mt5_grpc_client_csharp/scripts/check-generated.ps1 -Configuration Release
```

The script builds the package from `protos/*.proto` and fails if generated C#
bindings cannot be regenerated and compiled.
