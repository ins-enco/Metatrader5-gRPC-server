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
- Generated contract namespace: `MetaTrader.V1`
- Source contracts: `protos/*.proto`
- Generated artifacts: C# protobuf messages and gRPC client types generated
  from every proto file

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

The helper layer SHOULD stay thin and expose channel/client setup without
changing MT5 semantics.

```csharp
namespace MetaTrader.Grpc.Client;

public sealed class Mt5GrpcClientOptions
{
    public Uri Address { get; init; }
    public bool UseTls { get; init; }
    public TimeSpan? DefaultDeadline { get; init; }
    public int? MaxSendMessageSize { get; init; }
    public int? MaxReceiveMessageSize { get; init; }
}

public static class Mt5GrpcClientFactory
{
    public static GrpcChannel CreateChannel(Mt5GrpcClientOptions options);
    public static TClient CreateClient<TClient>(GrpcChannel channel)
        where TClient : ClientBase<TClient>;
}
```

Implementation MAY adjust exact helper signatures during coding if tests show a
better fit, but the public contract MUST still provide:

- reusable channel creation
- generated client creation
- TLS/insecure address handling
- deadline and cancellation support
- .NET Framework 4.8 sample configuration using WinHttpHandler

## Error Contract

The client library MUST expose transport and gRPC status failures as exceptions
or typed results that retain original status details. Responses that contain the
contract-defined `Error` message MUST remain visible to callers as successful
transport responses with MT5 error content.

## Compatibility Contract

- .NET Framework 4.8 applications MUST be able to reference the package through
  `netstandard2.0`.
- .NET Framework examples MUST use TLS and WinHttpHandler for HTTP/2 gRPC.
- Current MT5 operations are unary-only; documentation MUST not advertise
  existing MT5 bidirectional streaming operations.
- Future streaming RPCs MUST be generated and documented according to their
  proto declarations and runtime platform support.

## Contract Drift Check

A validation command MUST fail when generated C# files no longer match
`protos/*.proto`. The check may regenerate into a temporary directory or compare
generated outputs after running the documented generation command.
