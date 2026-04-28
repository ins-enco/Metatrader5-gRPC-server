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

**Rationale**: Microsoft documents Grpc.Net.Client as the .NET gRPC client
package and shows client types generated from `.proto` files. It supports unary,
server streaming, client streaming, and bidirectional streaming client call
patterns when the platform transport supports them.

**Alternatives considered**:
- gRPC C# core library: supports .NET Framework but Microsoft documents it as
  not supported by Microsoft, in maintenance mode, and not recommended for new
  applications.
- Custom HTTP/2 transport: unnecessary and high risk because the generated gRPC
  client stack already handles serialization, addressing, status, headers, and
  streaming call types.

## Decision: Document .NET Framework 4.8 transport limits explicitly

**Rationale**: Microsoft documents that .NET Framework gRPC over HTTP/2 has
limited support and requires WinHttpHandler. Requirements include Windows 11 or
Windows Server 2019 or later, System.Net.Http.WinHttpHandler 6.0.1 or later,
.NET Framework 4.6.1 or later, and TLS-only calls. Microsoft also documents
that Windows Server 2019 and 2022 support unary and server streaming but not
client or bidirectional streaming. The current project proto files are unary,
so .NET Framework 4.8 is acceptable for the initial MT5 operations, but future
streaming contracts must state platform support clearly.

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

## Decision: Add a thin factory/options layer over generated clients

**Rationale**: Generated clients provide the type-safe RPC surface. A small
library layer should centralize channel creation, TLS/insecure address handling,
WinHttpHandler configuration for .NET Framework samples, deadlines,
cancellation, and typed error guidance without wrapping every proto field.

**Alternatives considered**:
- Expose only generated clients: maximizes transparency but repeats connection
  and compatibility setup in every consumer.
- High-level domain facade for all MT5 workflows: too broad for the first
  client package and risks changing MT5 behavior semantics.

## Sources

- Microsoft Learn, "Call gRPC services with the .NET client":
  https://learn.microsoft.com/en-us/aspnet/core/grpc/client?view=aspnetcore-10.0
- Microsoft Learn, "Use gRPC client with .NET Standard 2.0":
  https://learn.microsoft.com/en-us/aspnet/core/grpc/netstandard?view=aspnetcore-10.0
