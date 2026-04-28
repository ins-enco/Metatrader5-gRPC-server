# Feature Specification: C# Client Library

**Feature Branch**: `001-csharp-client-library`
**Created**: 2026-04-28
**Status**: Draft
**Input**: User description: "Create client libraries based on the project's proto files for C# , High-performance binary protocol communication , Bi-directional streaming capabilities ,Type-safe API interface"

## Clarifications

### Session 2026-04-28

- Q: What should the public C# client API use as its primary failure-handling model? -> A: Expose generated gRPC clients plus convenience wrapper methods returning typed success/failure results.
- Q: How should C# client package compatibility be versioned against proto contracts and the MT5 gRPC server? -> A: Use independent client SemVer plus documented proto contract version/hash and tested server version range.
- Q: What observability behavior must the C# client library provide for calls and failures? -> A: Optional ILogger integration for connection, call failure, deadline/cancellation, and MT5 error events.
- Q: What deadline/timeout policy should the convenience wrapper methods use by default? -> A: No built-in timeout; support optional client-wide default deadline and per-call overrides.
- Q: How should the client handle insecure versus TLS connections by default? -> A: Allow insecure connections by default unless TLS options are supplied.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Build a C# Trading Client (Priority: P1)

A C# application developer can add the project client library, connect to an
existing MetaTrader 5 gRPC server, and call the contract-defined account,
symbol, market data, order, position, history, and terminal operations with
strongly typed requests and responses.

**Why this priority**: This is the primary value of the feature: enabling C#
applications to use the existing MT5 gRPC ecosystem without manually handling
raw protocol messages, including older .NET Framework 4.8 applications.

**Independent Test**: A developer creates a minimal C# application, connects to
a test server, requests account information and symbol data, and receives typed
responses or typed error information.

**Acceptance Scenarios**:

1. **Given** a reachable MT5 gRPC server and valid connection settings, **When**
   the developer requests account information through the C# client, **Then**
   the result exposes the same account fields and error shape defined by the
   project contract.
2. **Given** an unavailable server endpoint, **When** the developer attempts a
   request, **Then** the client returns a clear typed failure path without
   misrepresenting the request as successful.
3. **Given** an existing .NET Framework 4.8 application, **When** the developer
   adds the client library, **Then** the application can reference the package
   through a `netstandard2.0` compatible target.

---

### User Story 2 - Use High-Volume Binary Communication (Priority: P2)

A trading application developer can use the C# client for repeated market-data,
symbol, and order-related workflows while preserving the performance benefits of
the project's binary protocol communication.

**Why this priority**: Trading and market-data workflows depend on efficient
request/response handling and predictable overhead.

**Independent Test**: A benchmark or test harness performs repeated symbol,
tick, rate, and order-check workflows against a controlled server and compares
the observed behavior with the contract baseline.

**Acceptance Scenarios**:

1. **Given** a test server with deterministic market-data responses, **When** a
   client performs repeated market-data requests, **Then** responses are received
   without avoidable text serialization or loss of typed fields.
2. **Given** a server response containing large repeated result sets, **When**
   the client receives the response, **Then** all items are exposed in the
   documented order and with their contract-defined value types.

---

### User Story 3 - Work with Streaming Contract Methods (Priority: P3)

A C# application developer can use client surfaces for client, server, and
bi-directional streaming methods whenever the project contracts define such RPCs,
while current unary-only contracts remain represented accurately.

**Why this priority**: The project promises gRPC interoperability, including
streaming capabilities, but the client must stay faithful to the proto contracts
instead of inventing unsupported server behavior.

**Independent Test**: A contract fixture containing a streaming RPC is generated
or referenced during validation, and the C# client demonstrates sending and
receiving typed streaming messages with cancellation and error handling.

**Acceptance Scenarios**:

1. **Given** a contract-defined bi-directional streaming RPC, **When** the
   developer sends a stream of typed requests and receives typed responses,
   **Then** the client supports both directions without dropping messages or
   hiding stream errors.
2. **Given** the current project proto files contain only unary RPCs, **When**
   the library documentation describes available operations, **Then** it does
   not claim that currently unavailable streaming MT5 operations exist.

### Edge Cases

- The server is unreachable, refuses the connection, or terminates a call before
  returning a response.
- The server returns a valid transport response that contains an MT5 error
  payload.
- The client receives optional, repeated, timestamp, 64-bit identifier, or
  floating-point fields whose values must remain type-safe in C#.
- A proto file adds, removes, or renumbers fields before the C# client is
  regenerated.
- Streaming validation uses a contract fixture because the current project
  service definitions do not yet declare streaming RPCs.
- TLS settings are required for a deployment but the client is configured for an
  insecure endpoint.
- A legacy .NET Framework 4.8 application consumes the client package and must
  not be forced to upgrade to a newer .NET runtime.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The feature MUST provide a C# client library surface for every
  service and RPC currently defined by the project's proto files.
- **FR-002**: The client library MUST expose strongly typed request, response,
  repeated value, optional value, timestamp, numeric, identifier, and error
  fields matching the project contracts.
- **FR-003**: Developers MUST be able to configure server address, port, secure
  connection settings, cancellation, optional client-wide default deadlines,
  per-call deadline overrides, and call credentials where supported by the
  runtime environment; convenience wrapper methods MUST NOT impose a built-in
  timeout when no deadline is configured.
- **FR-003a**: The client library MUST allow insecure connections by default
  when no TLS options are supplied and MUST use TLS settings when they are
  explicitly configured.
- **FR-004**: The client library MUST preserve the project's binary protocol
  communication and MUST NOT require developers to serialize or parse raw text
  payloads for normal contract-defined calls.
- **FR-005**: The client library MUST expose the generated gRPC clients for
  advanced callers and convenience wrapper methods that return typed
  success/failure results for transport failures, server-side gRPC failures,
  and MT5 error payloads.
- **FR-006**: The client library MUST include usage examples for connection,
  account information, symbol information, market data, order validation, order
  submission, and error handling.
- **FR-007**: The client library MUST support client, server, and bi-directional
  streaming client patterns for any streaming RPC declared by the project
  contracts.
- **FR-008**: The client library MUST accurately report the current contract
  surface and MUST NOT document or expose MT5 streaming operations that are not
  declared by the proto files.
- **FR-009**: Contract regeneration MUST produce a repeatable C# client artifact
  from the project proto files and detect when generated outputs are stale.
- **FR-010**: The feature MUST version the C# client package with independent
  SemVer and document the generated proto contract version or hash plus the
  tested compatible MT5 gRPC server package version range.
- **FR-011**: The C# client library MUST build as a `netstandard2.0` compatible
  package so .NET Framework 4.8 and other compatible applications can consume
  it.
- **FR-012**: The client library MUST provide optional `ILogger` integration
  that records connection attempts, call failures, deadline or cancellation
  outcomes, and MT5 error payload events without requiring callers to adopt an
  OpenTelemetry dependency.

### Protocol and MT5 Contract Impact *(mandatory)*

- **Affected RPCs/Messages**: All services, RPCs, request messages, response
  messages, shared error messages, timestamp fields, optional fields, repeated
  fields, numeric fields, and identifier fields currently declared in
  `protos/*.proto`. No proto field additions, removals, or renumbering are
  required for this feature.
- **Compatibility Decision**: Backward-compatible client-library addition. The
  server contract remains unchanged unless a later feature explicitly adds new
  streaming RPCs.
- **MT5 Operation Mapping**: The client library maps to the existing contract
  coverage for initialize/login/version/shutdown, terminal info, account info,
  symbols, symbol info, symbol ticks, market data, market book, orders, order
  checks, order calculations, positions, trade submission, history orders, and
  deals. MT5 return codes and error payloads must remain visible to callers.
- **Cross-Language Type Notes**: Optional fields must preserve presence, repeated
  fields must preserve order, timestamps must preserve instant values, 64-bit
  tickets and identifiers must remain 64-bit values, floating-point market and
  price values must remain numeric values, and undocumented sentinel values must
  not be introduced by the client library.

### Key Entities *(include if feature involves data)*

- **C# Client Package**: The distributable library that C# developers use to
  connect to an MT5 gRPC server and call contract-defined operations.
- **Legacy .NET Application**: An existing .NET Framework 4.8 application that
  must consume the client library without a runtime upgrade.
- **Contract Operation**: A service method defined by the project proto files,
  including its request type, response type, error behavior, and call pattern.
- **Connection Configuration**: Host, port, security settings, optional
  client-wide default deadlines, per-call deadline overrides, cancellation
  behavior, and credentials needed to reach a server.
- **Typed Error Result**: The caller-visible success/failure wrapper result
  used by convenience methods to represent successful responses, transport
  errors, gRPC status errors, and MT5 error payloads.
- **Streaming Call**: A contract-declared call pattern that may send messages,
  receive messages, or do both over a single long-lived call.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A developer can complete a first successful account-info request
  from a new C# application in 10 minutes or less using the provided package and
  examples.
- **SC-002**: 100% of services and RPCs declared in the current project proto
  files are available through the C# client library.
- **SC-003**: Representative unary workflows add no more than 10% overhead
  compared with the baseline generated contract client in the same test
  environment.
- **SC-004**: 100% of documented examples either complete successfully against a
  test server or show the expected typed failure result.
- **SC-005**: Streaming client validation passes for client, server, and
  bi-directional streaming contract fixtures, and current project documentation
  accurately states that existing MT5 service definitions are unary-only.
- **SC-006**: No contract compatibility check reports stale or mismatched C#
  generated artifacts after regeneration from the project proto files.
- **SC-007**: A .NET Framework 4.8 sample or compatibility check can reference
  the delivered package and complete at least one typed client call against a
  test server.
- **SC-008**: With `ILogger` configured, tests can observe log entries for a
  failed connection attempt, a deadline or cancellation outcome, and an MT5
  error payload returned through a typed failure result.

## Assumptions

- The first release targets client-library support and does not add new
  MetaTrader 5 server RPCs.
- The current `protos/*.proto` files are the source of truth for the initial C#
  client surface.
- Existing unary MT5 operations remain unary until a separate contract change
  introduces streaming RPCs.
- Insecure connections are allowed by default when TLS options are not supplied;
  secure connection settings remain configurable because remote deployments may
  require TLS.
- Validation can use a test server, mocks, or contract fixtures rather than a
  live broker account.
- `netstandard2.0` is the minimum compatibility target for the initial package
  because support for older .NET Framework 4.8 applications is required.
