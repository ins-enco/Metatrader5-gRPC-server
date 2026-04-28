# Tasks: C# Client Library

**Input**: Design documents from `/specs/001-csharp-client-library/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/csharp-client-library.md, quickstart.md
**Tests**: Required because this feature adds a public package, generated bindings, helper APIs, packaging metadata, examples, compatibility behavior, performance validation, and documentation accuracy checks.

**Organization**: Tasks are grouped by user story so each story can be implemented and tested independently after the shared foundation is complete.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it touches different files and has no dependency on incomplete tasks in the same phase
- **[Story]**: User story label for story phases only
- Every task includes an exact file path

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the C# package workspace, solution, project shells, examples, and shared build metadata.

- [X] T001 Create the C# solution file in mt5_grpc_client_csharp/MetaTrader.Grpc.Client.sln
- [X] T002 Create the netstandard2.0 package project in mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/MetaTrader.Grpc.Client.csproj
- [X] T003 [P] Create shared build properties in mt5_grpc_client_csharp/Directory.Build.props
- [X] T004 [P] Create the unit test project in mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.Tests/MetaTrader.Grpc.Client.Tests.csproj
- [X] T005 [P] Create the contract test project in mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/MetaTrader.Grpc.Client.ContractTests.csproj
- [X] T006 [P] Create the compatibility test project in mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.CompatibilityTests/MetaTrader.Grpc.Client.CompatibilityTests.csproj
- [X] T007 [P] Create the .NET example project in mt5_grpc_client_csharp/examples/NetStandardClientExample/NetStandardClientExample.csproj
- [X] T008 [P] Create the .NET Framework 4.8 example project in mt5_grpc_client_csharp/examples/NetFramework48ClientExample/NetFramework48ClientExample.csproj
- [X] T009 Add all projects to the solution in mt5_grpc_client_csharp/MetaTrader.Grpc.Client.sln

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add shared generation, configuration, result, factory, and test infrastructure that every user story depends on.

**CRITICAL**: No user story work should begin until this phase is complete.

- [X] T010 Configure Grpc.Tools protobuf generation for ../../../protos/*.proto in mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/MetaTrader.Grpc.Client.csproj
- [X] T011 Configure package dependencies for Grpc.Net.Client, Google.Protobuf, Grpc.Tools, Grpc.Core.Api, and Microsoft.Extensions.Logging.Abstractions in mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/MetaTrader.Grpc.Client.csproj
- [X] T012 [P] Implement connection options and TLS options in mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/Mt5GrpcClientOptions.cs
- [X] T013 [P] Implement typed result and error types in mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/Mt5GrpcResult.cs
- [X] T014 [P] Implement client exception type in mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/Mt5GrpcClientException.cs
- [X] T015 Implement reusable channel and generated-client factory methods in mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/Mt5GrpcClientFactory.cs
- [X] T016 [P] Create shared test server fixture infrastructure in mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.Tests/Fixtures/GrpcTestServerFixture.cs
- [X] T017 [P] Create shared contract inspection helpers in mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/ProtoContractCatalog.cs
- [X] T018 [P] Create generated-artifact drift check script in mt5_grpc_client_csharp/scripts/check-generated.ps1

**Checkpoint**: Foundation ready. User story implementation can now proceed.

---

## Phase 3: User Story 1 - Build a C# Trading Client (Priority: P1) MVP

**Goal**: A C# developer can reference the package, connect to an MT5 gRPC server, call all current unary contract operations through generated clients or typed wrapper methods, and handle transport, gRPC, and MT5 error payload failures.

**Independent Test**: A minimal C# application connects to a test server, requests account information and symbol data, and receives typed responses or typed error information. A .NET Framework 4.8 sample can reference the package through netstandard2.0.

### Tests for User Story 1

- [X] T019 [P] [US1] Add generated surface contract tests for all 16 services and 31 unary RPCs in mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/GeneratedSurfaceTests.cs
- [X] T020 [P] [US1] Add channel security and deadline option tests in mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.Tests/Mt5GrpcClientFactoryTests.cs
- [X] T021 [P] [US1] Add typed success, transport failure, gRPC failure, and MT5 error payload mapping tests in mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.Tests/Mt5GrpcResultTests.cs
- [X] T022 [P] [US1] Add ILogger event tests for connection, failure, deadline, cancellation, and MT5 error payload paths in mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.Tests/Mt5GrpcLoggingTests.cs
- [X] T023 [P] [US1] Add .NET Framework 4.8 reference compatibility test in mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.CompatibilityTests/NetFramework48ReferenceTests.cs

### Implementation for User Story 1

- [X] T024 [US1] Implement wrapper client construction and generated service client exposure in mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/Mt5GrpcClient.cs
- [X] T025 [US1] Implement per-call deadline and cancellation handling in mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/Mt5GrpcCallOptions.cs
- [X] T026 [US1] Implement unary invocation result mapping in mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/Mt5GrpcUnaryInvoker.cs
- [X] T027 [US1] Implement MT5 error payload extraction for contract responses in mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/Mt5GrpcErrorMapper.cs
- [X] T028 [US1] Implement bounded logging helpers without secret or raw payload logging in mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/Mt5GrpcClientLogging.cs
- [X] T029 [US1] Implement initialize, login, version, shutdown, terminal, and account wrapper methods in mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/Mt5GrpcClient.InitializeAndAccount.cs
- [X] T030 [US1] Implement symbols, symbol info, symbol tick, market data, and market book wrapper methods in mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/Mt5GrpcClient.MarketData.cs
- [X] T031 [US1] Implement orders, order checks, order calculations, order send, positions, history orders, and deals wrapper methods in mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/Mt5GrpcClient.Trading.cs
- [X] T032 [US1] Add package metadata including SemVer, proto contract identity, and tested server range in mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/MetaTrader.Grpc.Client.csproj
- [X] T033 [US1] Add direct generated-client and wrapper-result usage documentation in mt5_grpc_client_csharp/README.md
- [X] T034 [US1] Add account, symbol, market data, order validation, order submission, and error handling example code in mt5_grpc_client_csharp/examples/NetStandardClientExample/Program.cs
- [X] T035 [US1] Add TLS plus WinHttpHandler .NET Framework 4.8 example code in mt5_grpc_client_csharp/examples/NetFramework48ClientExample/Program.cs

**Checkpoint**: User Story 1 is functional and independently testable as the MVP.

---

## Phase 4: User Story 2 - Use High-Volume Binary Communication (Priority: P2)

**Goal**: Repeated market-data, symbol, and order-related workflows preserve typed binary gRPC behavior and stay within the documented overhead target compared with direct generated clients.

**Independent Test**: A benchmark or test harness performs repeated symbol, tick, rate, and order-check workflows against a controlled server and verifies wrapper overhead stays within 10% of the direct generated-client baseline.

### Tests for User Story 2

- [X] T036 [P] [US2] Add repeated market data and symbol ordering tests in mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/RepeatedFieldContractTests.cs
- [X] T037 [P] [US2] Add no-text-serialization guard tests for wrapper calls in mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.Tests/BinaryProtocolTests.cs
- [X] T038 [P] [US2] Add direct-client versus wrapper benchmark tests in mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.Tests/PerformanceBudgetTests.cs

### Implementation for User Story 2

- [X] T039 [US2] Add deterministic high-volume response fixtures in mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/Fixtures/MarketDataResponseFixtures.cs
- [X] T040 [US2] Add benchmark project configuration in mt5_grpc_client_csharp/benchmarks/MetaTrader.Grpc.Client.Benchmarks/MetaTrader.Grpc.Client.Benchmarks.csproj
- [X] T041 [US2] Implement repeated symbol, tick, rate, and order-check benchmarks in mt5_grpc_client_csharp/benchmarks/MetaTrader.Grpc.Client.Benchmarks/UnaryWorkflowBenchmarks.cs
- [X] T042 [US2] Update unary invoker to avoid reflection or text serialization on hot paths in mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/Mt5GrpcUnaryInvoker.cs
- [X] T043 [US2] Document benchmark execution and 10% overhead validation in mt5_grpc_client_csharp/README.md

**Checkpoint**: User Story 2 is measurable independently without changing User Story 1 behavior.

---

## Phase 5: User Story 3 - Work with Streaming Contract Methods (Priority: P3)

**Goal**: The client surface supports generated gRPC streaming patterns when a proto contract declares them, while current MT5 documentation remains accurate that the existing service definitions are unary-only.

**Independent Test**: A contract fixture containing client, server, and bidirectional streaming RPCs is generated or referenced during validation, and documentation checks confirm no current MT5 streaming operations are advertised.

### Tests for User Story 3

- [X] T044 [P] [US3] Add streaming fixture proto with client, server, and bidirectional RPCs in mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/Fixtures/streaming_fixture.proto
- [X] T045 [P] [US3] Add streaming fixture generation settings in mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/MetaTrader.Grpc.Client.ContractTests.csproj
- [X] T046 [P] [US3] Add streaming client pattern tests for cancellation, disposal, and error propagation in mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/StreamingFixtureTests.cs
- [X] T047 [P] [US3] Add documentation accuracy tests for unary-only current MT5 services in mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/DocumentationAccuracyTests.cs

### Implementation for User Story 3

- [X] T048 [US3] Add generated streaming fixture usage examples for validation only in mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/StreamingFixtureUsage.cs
- [X] T049 [US3] Document generated streaming support and .NET Framework platform limits in mt5_grpc_client_csharp/README.md
- [X] T050 [US3] Document that current MT5 proto services are unary-only in mt5_grpc_client_csharp/README.md
- [X] T051 [US3] Align streaming claims in feature quickstart documentation in specs/001-csharp-client-library/quickstart.md

**Checkpoint**: Streaming support is validated for contract-declared streaming fixtures without inventing MT5 streaming operations.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final validation, release metadata, documentation alignment, and reproducibility checks across all stories.

- [X] T052 [P] Verify generation drift command documentation in specs/001-csharp-client-library/quickstart.md
- [X] T053 [P] Add package pack verification notes in mt5_grpc_client_csharp/README.md
- [X] T054 [P] Add release compatibility metadata notes in mt5_grpc_client_csharp/CHANGELOG.md
- [X] T055 [P] Add build and test workflow for the C# solution in .github/workflows/csharp-client.yml
- [X] T056 Run restore, build, test, pack, and drift-check command documentation updates in specs/001-csharp-client-library/quickstart.md
- [X] T057 Validate all user-story examples and record expected outputs in mt5_grpc_client_csharp/README.md

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies; can start immediately.
- **Foundational (Phase 2)**: Depends on Phase 1 and blocks all user stories.
- **User Story 1 (Phase 3)**: Depends on Phase 2 and is the MVP.
- **User Story 2 (Phase 4)**: Depends on Phase 2; can run after or alongside User Story 1 if shared files are coordinated.
- **User Story 3 (Phase 5)**: Depends on Phase 2; can run after or alongside other stories because it primarily uses fixture and documentation files.
- **Polish (Phase 6)**: Depends on the desired user stories being complete.

### User Story Dependencies

- **US1 (P1)**: No dependency on US2 or US3 after the foundation is complete.
- **US2 (P2)**: Uses the unary invoker from US1 for final performance validation, but its fixture and benchmark tasks can start after the foundation.
- **US3 (P3)**: No dependency on US1 or US2 after the foundation is complete.

### Within Each User Story

- Write tests first and verify they fail before implementation.
- Implement shared wrapper or fixture code before story examples and documentation.
- Validate each story at its checkpoint before moving to the next priority.

## Parallel Opportunities

- Phase 1 tasks T003 through T008 can run in parallel after T001 and T002 are defined.
- Phase 2 tasks T012, T013, T014, T016, T017, and T018 can run in parallel after package dependencies are configured.
- US1 tests T019 through T023 can run in parallel.
- US2 tests T036 through T038 can run in parallel, and benchmark scaffolding T040 can run alongside fixtures T039.
- US3 tests T044 through T047 can run in parallel because they touch separate fixture, project, test, and documentation-test files.
- Polish tasks T052 through T055 can run in parallel after story implementation stabilizes.

## Parallel Example: User Story 1

```text
Task: "Add generated surface contract tests for all 16 services and 31 unary RPCs in mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/GeneratedSurfaceTests.cs"
Task: "Add channel security and deadline option tests in mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.Tests/Mt5GrpcClientFactoryTests.cs"
Task: "Add typed success, transport failure, gRPC failure, and MT5 error payload mapping tests in mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.Tests/Mt5GrpcResultTests.cs"
Task: "Add ILogger event tests for connection, failure, deadline, cancellation, and MT5 error payload paths in mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.Tests/Mt5GrpcLoggingTests.cs"
```

## Parallel Example: User Story 2

```text
Task: "Add repeated market data and symbol ordering tests in mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/RepeatedFieldContractTests.cs"
Task: "Add no-text-serialization guard tests for wrapper calls in mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.Tests/BinaryProtocolTests.cs"
Task: "Add deterministic high-volume response fixtures in mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/Fixtures/MarketDataResponseFixtures.cs"
Task: "Add benchmark project configuration in mt5_grpc_client_csharp/benchmarks/MetaTrader.Grpc.Client.Benchmarks/MetaTrader.Grpc.Client.Benchmarks.csproj"
```

## Parallel Example: User Story 3

```text
Task: "Add streaming fixture proto with client, server, and bidirectional RPCs in mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/Fixtures/streaming_fixture.proto"
Task: "Add streaming fixture generation settings in mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/MetaTrader.Grpc.Client.ContractTests.csproj"
Task: "Add streaming client pattern tests for cancellation, disposal, and error propagation in mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/StreamingFixtureTests.cs"
Task: "Add documentation accuracy tests for unary-only current MT5 services in mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/DocumentationAccuracyTests.cs"
```

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 setup.
2. Complete Phase 2 foundation.
3. Complete Phase 3 User Story 1.
4. Stop and validate generated clients, wrapper results, examples, package metadata, and .NET Framework 4.8 reference compatibility.

### Incremental Delivery

1. Setup plus foundation establishes the package, generation, and shared test infrastructure.
2. US1 delivers the usable client library and examples.
3. US2 adds performance validation for high-volume binary workflows.
4. US3 adds streaming fixture validation and documentation accuracy.
5. Polish performs drift, package, CI, and quickstart validation.

### Suggested MVP Scope

Deliver Phase 1, Phase 2, and Phase 3 only. This provides generated clients, typed wrapper methods, logging, deadline/cancellation handling, examples, and .NET Framework 4.8 package compatibility.

## Notes

- The prerequisite script reported the current branch as `Csharp_client_lib`, while spec-kit expects a branch name like `001-csharp-client-library`.
- The feature directory used for this task list is `specs/001-csharp-client-library/` because it was provided explicitly in the user request.
- Current MT5 proto services are unary-only; streaming work is limited to generated fixture validation and documentation accuracy unless future proto contracts add streaming RPCs.
