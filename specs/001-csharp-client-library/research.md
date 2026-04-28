# Research: C# Client Library

## Decision: Target `netstandard2.0` for the client library

**Rationale**: The feature explicitly requires .NET Framework 4.8 support for
older applications. A `netstandard2.0` library is consumable by .NET Framework
4.8 while still allowing modern .NET applications to use the same package.

**Alternatives considered**:
- `net8.0` or `net9.0` only: simpler and faster runtime behavior, but excludes
  .NET Framework 4.8 applications.
- Multi-target `netstandard2.0` plus modern .NET: useful later for optimized
  implementations, but the initial compatibility contract can be satisfied with
  a single `netstandard2.0` target.

## Decision: Use Grpc.Net.Client as the primary client transport

**Rationale**: Microsoft documents `Grpc.Net.Client` as the .NET gRPC client
package and generated client types as the normal API produced from `.proto`
files. The client stack handles message serialization, addressing, status,
headers, channel options, and unary or streaming call patterns when the runtime
transport supports them.

**Alternatives considered**:
- gRPC C# core library: supports .NET Framework but Microsoft documents it as
  unsupported by Microsoft, in maintenance mode, and not recommended for new
  applications.
- Custom HTTP/2 transport: unnecessary and high risk because the generated gRPC
  client stack already handles protocol details.

## Decision: Expose generated clients plus typed wrapper results

**Rationale**: Generated clients preserve the exact proto contract and give
advanced callers direct access to standard gRPC behavior. Convenience wrappers
provide the clearer application-level failure model required by the spec by
returning typed success/failure results for transport failures, gRPC status
failures, and MT5 error payloads.

**Alternatives considered**:
- Generated clients only: transparent but leaves repeated error-handling logic
  to every consumer.
- Wrapper-only API: simpler for application developers but hides generated gRPC
  capabilities and risks changing MT5 semantics.
- Exceptions for all failures: consistent with gRPC transport errors but less
  suitable for expected MT5 error payloads.

## Decision: Allow insecure connections by default unless TLS is configured

**Rationale**: The current project is commonly used in local MT5 automation and
test environments where plaintext localhost endpoints are practical. The client
should create an insecure channel when no TLS options are supplied and switch to
TLS when the address or options request it. Documentation must still call out
that remote deployments may require TLS and that .NET Framework gRPC support is
TLS-only.

**Alternatives considered**:
- Require explicit security mode for every connection: reduces accidental
  plaintext use but adds ceremony to local/test usage.
- Require TLS for every connection: conflicts with local development and test
  server workflows.

## Decision: No built-in wrapper timeout

**Rationale**: MT5 operations and deployment latency vary, so the wrapper should
not silently change call behavior with a hidden timeout. It must support an
optional client-wide default deadline, per-call deadline overrides, and
cancellation tokens so callers can enforce their own policy.

**Alternatives considered**:
- Built-in default timeout for all unary wrapper calls: safer for runaway calls
  but may break slow deployments unexpectedly.
- Require every call to pass a deadline or cancellation token: explicit but too
  noisy for basic client usage.

## Decision: Provide optional `ILogger` integration, not mandatory telemetry

**Rationale**: `GrpcChannelOptions` supports `LoggerFactory`, and Microsoft
documents gRPC client logs and diagnostic events. An optional `ILogger` path
fits `netstandard2.0` consumers and satisfies failure diagnosability without
forcing OpenTelemetry or a specific observability stack.

**Alternatives considered**:
- No library-level observability: relies on application logging only and makes
  typed wrapper failures harder to diagnose.
- Built-in OpenTelemetry tracing and metrics: useful later, but heavier than
  required and not appropriate as a mandatory dependency for the first client
  package.

## Decision: Version the C# package independently with contract metadata

**Rationale**: Independent SemVer lets the C# client release wrapper fixes,
examples, and packaging improvements without requiring a server release. Each
package version must document the generated proto contract version or hash and
the tested compatible MT5 gRPC server package version range so compatibility is
auditable.

**Alternatives considered**:
- Lock client version exactly to the server package: easy to reason about but
  couples unrelated client-only fixes to server releases.
- Use proto contract version as the package version: makes contract identity
  prominent but leaves no clean place for client-only SemVer changes.

## Decision: Document .NET Framework 4.8 transport limits explicitly

**Rationale**: Microsoft documents that .NET Framework gRPC over HTTP/2 has
limited support and requires `WinHttpHandler`. Requirements include Windows 11
or later, Windows Server 2019 or later, `System.Net.Http.WinHttpHandler` 6.0.1
or later, .NET Framework 4.6.1 or later, and TLS-only calls. Windows Server 2019
and 2022 support unary and server streaming but not client or bidirectional
streaming. The current project proto files are unary, so .NET Framework 4.8 is
acceptable for the initial MT5 operations, but future streaming contracts must
state platform support clearly.

**Alternatives considered**:
- Promise bidirectional streaming for all .NET Framework 4.8 hosts: rejected
  because it conflicts with documented platform limitations.
- Require modern .NET for streaming only: compatible with the feature if future
  streaming contracts are added; document this as a compatibility constraint.

## Decision: Generate C# types directly from `protos/*.proto`

**Rationale**: The constitution defines proto files as the product contract.
Generated C# messages and service clients preserve field numbers, optional
presence, repeated value ordering, timestamps, 64-bit identifiers, numeric
values, and service method names without hand-maintained duplication.

**Alternatives considered**:
- Handwritten DTOs over generated clients: adds mapping risk and can hide proto
  semantics from C# callers.
- Editing generated C# files directly: violates the constitution and makes
  regeneration unreliable.

## Sources

- Microsoft Learn, "Call gRPC services with the .NET client":
  https://learn.microsoft.com/en-us/aspnet/core/grpc/client?view=aspnetcore-10.0
- Microsoft Learn, "Use gRPC client with .NET Standard 2.0":
  https://learn.microsoft.com/en-us/aspnet/core/grpc/netstandard?view=aspnetcore-9.0
- Microsoft Learn, "gRPC for .NET configuration":
  https://learn.microsoft.com/en-us/aspnet/core/grpc/configuration?view=aspnetcore-10.0
- Microsoft Learn, "Logging and diagnostics in gRPC on .NET":
  https://learn.microsoft.com/en-us/aspnet/core/grpc/diagnostics?view=aspnetcore-10.0
