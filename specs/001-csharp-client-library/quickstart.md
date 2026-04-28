# Quickstart: C# Client Library

## Prerequisites

- .NET SDK 9.0 or later for repository builds
- Windows host for .NET Framework 4.8 compatibility validation
- Reachable MT5 gRPC server for live smoke tests, or a test server fixture
- Existing proto contracts in `protos/*.proto`

## Build the package

```powershell
dotnet restore mt5_grpc_client_csharp/MetaTrader.Grpc.Client.sln
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

- unit tests pass for options, channel creation, and error handling
- contract tests pass for all current services and RPCs
- compatibility tests verify a .NET Framework 4.8 sample can reference the
  `netstandard2.0` package and complete a typed unary call against a test server

## Use from a modern C# application

```csharp
using MetaTrader.Grpc.Client;
using MetaTrader.V1;

var options = new Mt5GrpcClientOptions
{
    Address = new Uri("https://localhost:50051"),
    UseTls = true,
    DefaultDeadline = TimeSpan.FromSeconds(5)
};

using var channel = Mt5GrpcClientFactory.CreateChannel(options);
var accountClient = new AccountInfoService.AccountInfoServiceClient(channel);
var response = await accountClient.GetAccountInfoAsync(
    new AccountInfoRequest(),
    deadline: DateTime.UtcNow.AddSeconds(5));

if (response.Error is not null && response.Error.Code != 0)
{
    Console.WriteLine($"MT5 error {response.Error.Code}: {response.Error.Message}");
}
else
{
    Console.WriteLine(response.AccountInfo.Login);
}
```

## Use from .NET Framework 4.8

.NET Framework 4.8 consumers use the same `netstandard2.0` package, but the
transport has stricter requirements. Use TLS and configure WinHttpHandler in the
sample application.

```csharp
using Grpc.Net.Client;
using MetaTrader.V1;
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
