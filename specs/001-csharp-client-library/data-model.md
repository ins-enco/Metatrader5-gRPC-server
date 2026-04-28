# Data Model: C# Client Library

## C# Client Package

**Description**: Distributable package that exposes generated C# protobuf/gRPC
types plus helper APIs for connecting to an MT5 gRPC server.

**Fields**:
- `PackageId`: stable package identifier, planned as `MetaTrader.Grpc.Client`
- `TargetFramework`: `netstandard2.0`
- `ProtoSourcePath`: source proto files under `protos/`
- `GeneratedOutputPath`: generated C# files under the C# package
- `Version`: package version aligned with proto/server compatibility notes

**Validation rules**:
- MUST target `netstandard2.0`.
- MUST include generated bindings for every current service in `protos/*.proto`.
- MUST be reproducible from documented generation commands.
- MUST NOT require modifying `protos/*.proto` for this feature.

## Contract Operation

**Description**: One RPC declared by the project proto files.

**Fields**:
- `ServiceName`: proto service name, such as `AccountInfoService`
- `RpcName`: RPC method name, such as `GetAccountInfo`
- `RequestType`: generated C# request message
- `ResponseType`: generated C# response message
- `CallPattern`: unary for all current MT5 operations
- `Mt5Operation`: corresponding MT5 behavior represented by the server
- `ErrorShape`: transport status and/or contract-defined `Error` payload

**Validation rules**:
- MUST map one-to-one to the proto contract.
- MUST preserve request and response type names from generated code.
- MUST preserve error payload visibility.
- MUST document if a future operation uses server, client, or bidirectional
  streaming.

## Connection Configuration

**Description**: User-provided settings used to create a reusable gRPC channel
and generated clients.

**Fields**:
- `Address`: server URI including scheme, host, and port
- `UseTls`: whether the channel is expected to use TLS
- `Deadline`: optional per-call or default deadline
- `CancellationToken`: optional cancellation token for async calls
- `Credentials`: optional call or channel credentials
- `HttpHandler`: optional handler override, including WinHttpHandler for .NET
  Framework compatibility samples
- `MaxSendMessageSize`: optional send size bound
- `MaxReceiveMessageSize`: optional receive size bound

**Validation rules**:
- .NET Framework compatibility documentation MUST show TLS with WinHttpHandler.
- Insecure endpoints MUST be documented as local/development only.
- Deadlines and cancellation MUST be available to callers.
- Handler configuration MUST be explicit for .NET implementations without full
  HTTP/2 support.

## Typed Error Result

**Description**: Caller-visible failure representation for transport, gRPC
status, and MT5 error payloads.

**Fields**:
- `StatusCode`: transport or gRPC status when available
- `Message`: human-readable failure message
- `Trailers`: optional gRPC trailers
- `Mt5ErrorCode`: contract-defined MT5 error code when present
- `Mt5ErrorMessage`: contract-defined MT5 error message when present
- `Operation`: service and method that failed

**Validation rules**:
- MUST distinguish transport failures from successful responses containing MT5
  errors.
- MUST NOT silently coerce failures into success responses.
- MUST preserve original exception/status details for diagnostics.

## Legacy .NET Application

**Description**: Existing .NET Framework 4.8 application that references the
client package.

**Fields**:
- `TargetFramework`: `.NET Framework 4.8`
- `OperatingSystem`: Windows host running the application
- `TransportSupport`: platform-specific support for HTTP/2 gRPC
- `ReferenceMode`: package reference to the C# client library

**Validation rules**:
- MUST be able to reference the `netstandard2.0` client package.
- MUST have a compatibility check that performs at least one typed unary call.
- MUST document that client and bidirectional streaming support depends on host
  platform support and is not guaranteed on all .NET Framework deployments.

## Streaming Call

**Description**: Future contract-declared RPC pattern that streams requests,
responses, or both.

**Fields**:
- `StreamingType`: server, client, or bidirectional
- `RequestMessage`: streamed or initial request message
- `ResponseMessage`: streamed or final response message
- `Cancellation`: call cancellation/disposal behavior
- `PlatformSupport`: runtime support notes, especially for .NET Framework

**Validation rules**:
- MUST only be exposed when declared by a proto contract.
- MUST preserve request/response ordering and typed message access.
- MUST document cancellation and disposal behavior.
- MUST document .NET Framework platform limitations.
