# Implementation Plan: C# Client Library

**Branch**: `Csharp_client_lib` | **Date**: 2026-04-28 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-csharp-client-library/spec.md`

## Summary

Create a C# client library package from the repository's existing
`protos/*.proto` contracts so C# and .NET Framework 4.8 applications can call
the MT5 gRPC server through a type-safe API. The implementation adds a
`netstandard2.0` client package, generated C# protobuf/gRPC bindings,
connection configuration helpers, convenience wrapper methods returning typed
success/failure results, optional `ILogger` integration, examples,
compatibility checks, and tests. No server RPC or proto field changes are
planned for this feature.

## Technical Context

**Language/Version**: C# targeting `netstandard2.0`; repository builds with installed .NET SDK 9.0.307
**Primary Dependencies**: Grpc.Net.Client, Google.Protobuf, Grpc.Tools, Microsoft.Extensions.Logging.Abstractions, System.Net.Http.WinHttpHandler for .NET Framework compatibility samples
**Storage**: N/A
**Testing**: `dotnet test`; contract generation/drift check; package build; .NET Framework 4.8 compatibility sample or check; optional unary benchmark harness
**Target Platform**: NuGet-style C# library for .NET implementations compatible with `netstandard2.0`, with explicit .NET Framework 4.8 support constraints
**Project Type**: Library/client SDK plus examples and tests
**Performance Goals**: Representative unary workflows add no more than 10% overhead over direct generated gRPC clients in the same environment
**Constraints**: Existing proto contracts remain source of truth; current MT5 service definitions are unary-only; wrapper methods impose no built-in timeout unless callers configure a client-wide default deadline or per-call deadline; insecure connections are allowed by default when TLS options are absent; .NET Framework gRPC requires TLS and WinHttpHandler and has platform-specific streaming limits
**Scale/Scope**: Covers all current services and RPCs in `protos/*.proto`: 16 services and 31 unary RPCs

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Protocol contract**: PASS. `Protocol and MT5 Contract Impact` in the spec
  identifies all `protos/*.proto` services, RPCs, messages, and field classes as
  affected without proto additions, removals, or renumbering. The plan requires
  repeatable generated C# artifacts and a drift check.
- **MT5 behavior fidelity**: PASS. The client maps directly to existing MT5 RPC
  coverage and keeps MT5 return codes and error payloads visible through
  generated clients and typed wrapper results.
- **Multi-language type safety**: PASS. The library is generated from protobuf
  definitions and preserves optional fields, repeated order, timestamps, 64-bit
  identifiers, numeric values, and contract-defined error messages.
- **Reliability and observability**: PASS. The plan covers unreachable servers,
  gRPC failures, MT5 error payloads, cancellation, optional deadlines, TLS and
  insecure modes, optional `ILogger` integration, and streaming disposal and
  cancellation guidance for future streaming contracts.
- **Test and release reproducibility**: PASS. The plan requires generation,
  unit, contract, compatibility, package, example, and documentation accuracy
  checks, with commands documented in `quickstart.md`.

Post-design re-check: PASS. `research.md`, `data-model.md`,
`contracts/csharp-client-library.md`, and `quickstart.md` document the
compatibility limits, wrapper result contract, default deadline and security
decisions, package compatibility metadata, validation commands, and
no-proto-change decision.

## Project Structure

### Documentation (this feature)

```text
specs/001-csharp-client-library/
|-- plan.md
|-- research.md
|-- data-model.md
|-- quickstart.md
|-- contracts/
|   `-- csharp-client-library.md
|-- checklists/
|   `-- requirements.md
`-- tasks.md             # created later by /speckit-tasks
```

### Source Code (repository root)

```text
mt5_grpc_client_csharp/
|-- README.md
|-- MetaTrader.Grpc.Client.sln
|-- src/
|   `-- MetaTrader.Grpc.Client/
|       |-- MetaTrader.Grpc.Client.csproj
|       |-- Generated/
|       |-- Mt5GrpcClientOptions.cs
|       |-- Mt5GrpcClientFactory.cs
|       |-- Mt5GrpcResult.cs
|       |-- Mt5GrpcError.cs
|       `-- Mt5GrpcClientException.cs
|-- tests/
|   |-- MetaTrader.Grpc.Client.Tests/
|   |-- MetaTrader.Grpc.Client.ContractTests/
|   `-- MetaTrader.Grpc.Client.CompatibilityTests/
`-- examples/
    |-- NetStandardClientExample/
    `-- NetFramework48ClientExample/

protos/
`-- *.proto
```

**Structure Decision**: Add a third root package directory,
`mt5_grpc_client_csharp/`, matching the repository's existing package-oriented
layout (`mt5_grpc_proto/`, `mt5_grpc_server/`) while keeping C# source under
standard `src/`, `tests/`, and `examples/` subdirectories. Generated C# code
stays under the C# package so Python generated bindings remain untouched.

## Complexity Tracking

No constitution violations.
