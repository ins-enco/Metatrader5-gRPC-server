# Quickstart: C# Client Library

## Prerequisites

- .NET SDK 9.0 or later for repository builds
- Windows host for .NET Framework 4.8 compatibility validation
- Reachable MT5 gRPC server for live smoke tests, or a test server fixture
- Existing proto contracts in `protos/*.proto`

## Build the package

```powershell
dotnet restore mt5_grpc_client_csharp/MetaTrader.Grpc.Client.sln --source https://api.nuget.org/v3/index.json
dotnet build mt5_grpc_client_csharp/MetaTrader.Grpc.Client.sln -c Release
```

Expected result: the `MetaTrader.Grpc.Client` project builds for
`netstandard2.0`.

## Regenerate C# contract bindings

```powershell
dotnet build mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/MetaTrader.Grpc.Client.csproj -c Release
```

Expected result: generated C# protobuf and gRPC client types reflect every
service and message in `protos/*.proto`.

## Run tests

```powershell
dotnet test mt5_grpc_client_csharp/MetaTrader.Grpc.Client.sln -c Release
```

Expected result:

- unit tests pass for options, channel creation, wrapper result mapping,
  deadline behavior, security mode selection, and `ILogger` events
- contract tests pass for all current services and RPCs
- compatibility tests verify a .NET Framework 4.8 sample can reference the
  `netstandard2.0` package and complete a typed unary call against a test server
- generation or drift checks report no stale C# generated artifacts

## Use generated clients directly

```csharp
using MetaTrader.Grpc.Client;
using Metatrader.V1;

var options = new Mt5GrpcClientOptions
{
    Address = new Uri("http://localhost:50051")
};

using var channel = Mt5GrpcClientFactory.CreateChannel(options);
var accountClient = new AccountInfoService.AccountInfoServiceClient(channel);

var response = await accountClient.GetAccountInfoAsync(
    new AccountInfoRequest(),
    cancellationToken: CancellationToken.None);

if (response.Error is not null && response.Error.Code != 0)
{
    Console.WriteLine($"MT5 error {response.Error.Code}: {response.Error.Message}");
}
else
{
    Console.WriteLine(response.AccountInfo.Login);
}
```

Expected result: the default configuration can call a local insecure endpoint
when no TLS options are supplied.

## Use convenience wrapper results

```csharp
using MetaTrader.Grpc.Client;

var options = new Mt5GrpcClientOptions
{
    Address = new Uri("http://localhost:50051"),
    DefaultDeadline = TimeSpan.FromSeconds(5)
};

using var client = Mt5GrpcClientFactory.Create(options);

var result = await client.GetAccountInfoAsync(
    deadline: DateTime.UtcNow.AddSeconds(2),
    cancellationToken: CancellationToken.None);

if (!result.IsSuccess)
{
    Console.WriteLine($"{result.Error.Operation}: {result.Error.Message}");
    return;
}

Console.WriteLine(result.Value.AccountInfo.Login);
```

Expected result: wrapper methods return typed success/failure results for
transport failures, gRPC status failures, and MT5 error payloads. No timeout is
applied unless `DefaultDeadline` or a per-call deadline is configured.

## Enable client logging

```csharp
using Microsoft.Extensions.Logging;
using MetaTrader.Grpc.Client;

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

var options = new Mt5GrpcClientOptions
{
    Address = new Uri("http://localhost:50051"),
    LoggerFactory = loggerFactory
};

using var client = Mt5GrpcClientFactory.Create(options);
```

Expected result: configured logs include connection attempts, call failures,
deadline or cancellation outcomes, and MT5 error payload events.

## Use TLS

```csharp
var options = new Mt5GrpcClientOptions
{
    Address = new Uri("https://mt5-grpc.example.com:50051"),
    TlsOptions = Mt5GrpcTlsOptions.SystemTrust()
};
```

Expected result: TLS settings are used when supplied. Remote deployments should
configure TLS according to their trust boundary and credential requirements.

## Use from .NET Framework 4.8

.NET Framework 4.8 consumers use the same `netstandard2.0` package, but the
transport has stricter requirements. Use TLS and configure WinHttpHandler in the
sample application.

```csharp
using Grpc.Net.Client;
using Metatrader.V1;
using System;
using System.Net.Http;
using System.Threading.Tasks;

var handler = new WinHttpHandler();
var channel = GrpcChannel.ForAddress(
    "https://localhost:50051",
    new GrpcChannelOptions { HttpHandler = handler });

var client = new AccountInfoService.AccountInfoServiceClient(channel);
var response = await client.GetAccountInfoAsync(new AccountInfoRequest());
Console.WriteLine(response.AccountInfo.Login);
```

Expected result: a .NET Framework 4.8 app can reference the package and perform
current unary MT5 calls. Future client or bidirectional streaming calls must be
validated against the target Windows platform because .NET Framework HTTP/2
support is limited.

## Validate documentation accuracy

```powershell
rg "stream|streaming" protos specs/001-csharp-client-library mt5_grpc_client_csharp
```

Expected result: docs state that current MT5 proto services are unary-only and
only describe streaming support for contract-declared streaming RPCs or fixtures.

## Validate package compatibility metadata

```powershell
dotnet pack mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/MetaTrader.Grpc.Client.csproj -c Release
```

Expected result: the package metadata documents independent client SemVer, the
generated proto contract version or hash, and the tested compatible MT5 gRPC
server package version range.

## Validate generated binding drift

```powershell
mt5_grpc_client_csharp/scripts/check-generated.ps1 -Configuration Release
```

Expected result: the C# client project builds from `protos/*.proto` and reports
that generated C# bindings match the current proto inputs.
