# Data Model: C# Client Library

## C# Client Package

**Description**: Distributable package that exposes generated C# protobuf/gRPC
types plus helper APIs for connecting to an MT5 gRPC server.

**Fields**:
- `PackageId`: stable package identifier, planned as `MetaTrader.Grpc.Client`
- `TargetFramework`: `netstandard2.0`
- `ProtoSourcePath`: source proto files under `protos/`
- `GeneratedOutputPath`: generated C# files under the C# package
- `Version`: independent client SemVer
- `ProtoContractIdentity`: generated proto contract version or hash
- `TestedServerVersionRange`: MT5 gRPC server package versions validated with
  the package

**Validation rules**:
- MUST target `netstandard2.0`.
- MUST include generated bindings for every current service in `protos/*.proto`.
- MUST document package SemVer, proto contract identity, and tested server
  version range.
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
- MUST preserve MT5 error payload visibility.
- MUST document if a future operation uses server, client, or bidirectional
  streaming.

## Connection Configuration

**Description**: User-provided settings used to create a reusable gRPC channel,
generated clients, and wrapper clients.

**Fields**:
- `Address`: server URI including scheme, host, and port
- `TlsOptions`: optional TLS configuration; absence means insecure connection
  by default
- `DefaultDeadline`: optional client-wide default deadline
- `PerCallDeadline`: optional per-call deadline override
- `CancellationToken`: optional cancellation token for async calls
- `Credentials`: optional call or channel credentials
- `LoggerFactory`: optional `ILogger` provider for channel and wrapper logs
- `HttpHandler`: optional handler override, including WinHttpHandler for .NET
  Framework compatibility samples
- `MaxSendMessageSize`: optional send size bound
- `MaxReceiveMessageSize`: optional receive size bound

**Validation rules**:
- Insecure endpoints MUST be allowed by default when TLS options are absent.
- TLS settings MUST be used when explicitly configured.
- .NET Framework compatibility documentation MUST show TLS with WinHttpHandler.
- Convenience wrapper methods MUST NOT impose a built-in timeout when no
  deadline is configured.
- Client-wide default deadlines, per-call deadline overrides, and cancellation
  MUST be available to callers.
- Handler configuration MUST be explicit for .NET implementations without full
  HTTP/2 support.

## Generated Client Surface

**Description**: Generated gRPC service clients and protobuf message types
created directly from `protos/*.proto`.

**Fields**:
- `GeneratedNamespace`: generated C# namespace for proto messages and clients
- `Services`: all current proto service clients
- `Messages`: request, response, shared error, timestamp, optional, and repeated
  message types
- `RegenerationCommand`: documented command used to reproduce generated output

**Validation rules**:
- MUST expose 100% of current proto services and RPCs.
- MUST preserve protobuf field numbers, presence, repeated ordering, timestamp
  values, numeric types, and 64-bit identifiers.
- MUST fail a drift check when generated files do not match current proto
  inputs.

## Convenience Wrapper Method

**Description**: Application-facing helper call that wraps a generated unary
client call and returns a typed success/failure result.

**Fields**:
- `Operation`: service and RPC being called
- `Request`: generated request message
- `SuccessValue`: generated response message
- `FailureValue`: typed error result
- `Deadline`: effective optional deadline after combining client default and
  per-call override
- `CancellationToken`: per-call cancellation token
- `Logger`: optional logger used for connection, failure, deadline,
  cancellation, and MT5 error events

**Validation rules**:
- MUST preserve generated request and response types.
- MUST distinguish transport or gRPC failures from successful transport
  responses that contain MT5 error payloads.
- MUST log configured diagnostic events without requiring OpenTelemetry.
- MUST NOT hide generated clients from advanced callers.

## Typed Error Result

**Description**: Caller-visible failure representation returned by convenience
methods for transport, gRPC status, and MT5 error payloads.

**Fields**:
- `StatusCode`: transport or gRPC status when available
- `Message`: human-readable failure message
- `Trailers`: optional gRPC trailers
- `Mt5ErrorCode`: contract-defined MT5 error code when present
- `Mt5ErrorMessage`: contract-defined MT5 error message when present
- `Operation`: service and method that failed
- `Exception`: original exception when available

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
- `HttpHandler`: WinHttpHandler for gRPC over HTTP/2

**Validation rules**:
- MUST be able to reference the `netstandard2.0` client package.
- MUST have a compatibility check that performs at least one typed unary call.
- MUST document that .NET Framework gRPC calls require TLS and WinHttpHandler.
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
