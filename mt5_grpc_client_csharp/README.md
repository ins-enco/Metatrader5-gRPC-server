# MetaTrader.Grpc.Client

`MetaTrader.Grpc.Client` is a `netstandard2.0` C# client package generated from
the repository's `protos/*.proto` contracts. It exposes the generated gRPC
clients for advanced callers and a thin wrapper that returns typed
`Mt5GrpcResult<T>` values for convenience calls.

Package metadata uses independent client SemVer. The initial package version is
`0.1.0`, with proto contract identity `protos-001-csharp-client-library` and a
tested server range of `[0.1.0,1.0.0)`.

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

## Streaming

Current MT5 proto services are unary-only. The package does not document or
invent current MT5 streaming operations. If future proto contracts add server,
client, or bidirectional streaming RPCs, Grpc.Tools will generate typed streaming
client methods and tests must validate cancellation, disposal, error propagation,
and runtime platform support.

.NET Framework 4.8 consumers can reference the `netstandard2.0` package, but
gRPC over HTTP/2 requires TLS and `WinHttpHandler`. Client and bidirectional
streaming support depends on the Windows host and is not guaranteed on all .NET
Framework deployments.

## Drift Check

```powershell
mt5_grpc_client_csharp/scripts/check-generated.ps1 -Configuration Release
```

The script builds the package from `protos/*.proto` and fails if generated C#
bindings cannot be regenerated and compiled.
