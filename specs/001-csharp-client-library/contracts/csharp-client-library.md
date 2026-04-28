# Contract: C# Client Library

## Scope

The C# client package exposes the repository's existing MetaTrader 5 gRPC
contract to C# consumers. The proto files remain the source of truth. This
feature does not add, remove, or renumber proto fields and does not add server
RPCs.

## Package Contract

- Package ID: `MetaTrader.Grpc.Client`
- Target framework: `netstandard2.0`
- Primary namespace: `MetaTrader.Grpc.Client`
- Generated contract namespace: `Metatrader.V1`
- Source contracts: `protos/*.proto`
- Generated artifacts: C# protobuf messages and gRPC client types generated
  from every proto file
- Versioning: independent client SemVer
- Compatibility metadata: generated proto contract version or hash plus tested
  compatible MT5 gRPC server package version range

## Public Client Surface

The package MUST expose generated C# clients for all current services:

- `MetaTraderService`
- `InitializeService`
- `TerminalInfoService`
- `AccountInfoService`
- `SymbolsService`
- `SymbolInfoService`
- `SymbolInfoTickService`
- `MarketDataService`
- `MarketBookService`
- `OrdersService`
- `OrderCheckService`
- `OrderCalcService`
- `OrderSendService`
- `PositionsService`
- `HistoryOrdersService`
- `TradeHistoryService`

All current RPCs are unary and MUST remain represented as unary methods in the
C# generated clients.

## Helper API Contract

The helper layer stays thin and exposes channel/client setup plus convenience
wrapper methods without changing MT5 semantics.

```csharp
namespace MetaTrader.Grpc.Client;

public sealed class Mt5GrpcClientOptions
{
    public Uri Address { get; set; }
    public Mt5GrpcTlsOptions TlsOptions { get; set; }
    public TimeSpan? DefaultDeadline { get; set; }
    public int? MaxSendMessageSize { get; set; }
    public int? MaxReceiveMessageSize { get; set; }
    public ILoggerFactory LoggerFactory { get; set; }
    public HttpMessageHandler HttpHandler { get; set; }
}

public static class Mt5GrpcClientFactory
{
    public static GrpcChannel CreateChannel(Mt5GrpcClientOptions options);
    public static TClient CreateClient<TClient>(GrpcChannel channel)
        where TClient : ClientBase<TClient>;
    public static Mt5GrpcClient Create(Mt5GrpcClientOptions options);
}

public sealed class Mt5GrpcResult<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public Mt5GrpcError? Error { get; }
}

public sealed class Mt5GrpcError
{
    public string Operation { get; init; }
    public StatusCode? StatusCode { get; init; }
    public string Message { get; init; }
    public Metadata? Trailers { get; init; }
    public int? Mt5ErrorCode { get; init; }
    public string? Mt5ErrorMessage { get; init; }
    public Exception? Exception { get; init; }
}
```

Implementation MAY adjust exact helper signatures during coding if tests show a
better fit, but the public contract MUST still provide:

- reusable channel creation
- generated client creation
- generated clients exposed for advanced callers
- convenience wrapper methods returning typed success/failure results
- insecure connection default when TLS options are absent
- TLS configuration when TLS options are supplied
- optional client-wide default deadline and per-call deadline overrides
- cancellation support
- optional `ILogger` integration
- .NET Framework 4.8 sample configuration using WinHttpHandler and TLS

## Error Contract

Generated clients retain standard gRPC behavior for advanced callers.
Convenience wrapper methods MUST return `Mt5GrpcResult<T>`-style typed results:

- successful transport responses without MT5 error payloads produce success
  values
- transport failures and gRPC status failures produce typed failure results
  that preserve status, trailers, message, operation, and original exception
  when available
- successful transport responses containing contract-defined MT5 error payloads
  produce typed failure results that preserve MT5 error code and message

The library MUST NOT silently coerce transport or MT5 failures into success
results.

## Deadline and Cancellation Contract

- Wrapper methods MUST NOT impose a built-in timeout when no deadline is
  configured.
- `DefaultDeadline` provides an optional client-wide policy.
- A per-call deadline overrides the client-wide default for that call.
- Cancellation tokens must be accepted by wrapper methods and forwarded to the
  underlying generated gRPC call.
- Deadline and cancellation outcomes must be observable through typed failures
  and configured logs.

## Observability Contract

When `ILoggerFactory` is configured, the library MUST emit useful diagnostic log
events for:

- connection attempts or channel creation failures
- transport and gRPC call failures
- deadline exceeded or cancellation outcomes
- MT5 error payloads returned through typed failure results

Logs MUST avoid exposing secrets, account credentials, or raw production payloads
that are not needed for diagnosis.

## Compatibility Contract

- .NET Framework 4.8 applications MUST be able to reference the package through
  `netstandard2.0`.
- .NET Framework examples MUST use TLS and WinHttpHandler for HTTP/2 gRPC.
- Current MT5 operations are unary-only; documentation MUST not advertise
  existing MT5 bidirectional streaming operations.
- Future streaming RPCs MUST be generated and documented according to their
  proto declarations and runtime platform support.
- Package metadata and release documentation MUST include independent client
  SemVer, generated proto contract version or hash, and tested compatible server
  package version range.

## Contract Drift Check

A validation command MUST fail when generated C# files no longer match
`protos/*.proto`. The check may regenerate into a temporary directory or compare
generated outputs after running the documented generation command.
