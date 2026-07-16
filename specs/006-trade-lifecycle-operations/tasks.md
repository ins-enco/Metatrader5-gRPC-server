# Tasks: Trade Lifecycle Operations

**Input**: Design documents from `/specs/006-trade-lifecycle-operations/`
**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/csharp-trade-lifecycle.md`, `quickstart.md`

**Tests**: Tests are required because this feature adds public behavior, financially sensitive request mapping, batch orchestration, package surface, and documentation examples. Write each story's tests first and confirm they fail for the expected missing behavior before implementation.

**Organization**: Tasks are grouped by user story so each story can be implemented and tested as an independently useful increment. Shared result, classification, transport-seam, deadline, and logging infrastructure is isolated in the foundational phase.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel with other tasks in the same phase because it changes a different file and has no dependency on incomplete work
- **[Story]**: Maps a task to User Story 1, 2, 3, or 4 from `spec.md`
- Every task names the exact file or project path it affects or verifies

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish a clean, reproducible baseline for the existing C# client before adding the lifecycle surface.

- [X] T001 Restore and run the existing baseline tests for `mt5_grpc_client_csharp/MetaTrader.Grpc.Client.sln`, recording any pre-existing failures before feature work
- [X] T002 Run `mt5_grpc_client_csharp/scripts/check-generated.ps1` in Release mode and confirm `protos/trade.proto` and `protos/position.proto` require no generated-binding changes

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add the shared domain types, execution classifier, internal test seam, deadline capture, and bounded logging required by every user story.

**CRITICAL**: No user-story implementation begins until this phase is complete.

- [X] T003 [P] Create immutable/snapshotted `OpenOrderRequest`, `ModifyTradeRequest`, `PositionModification`, `PendingOrderModification`, `CloseByRequest`, and `ClosePositionsByRequest` DTOs with constructor-required fields and optional values in `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/TradeLifecycleRequests.cs`
- [X] T004 [P] Create `TradeLifecycleOperation`, `TradeExecutionStatus`, `TradeOperationResult`, `MultipleCloseByStatus`, `PairAttemptState`, `PositionRemainderReason`, `MultipleCloseByResult`, `CloseByPairOutcome`, and `PositionRemainder` with copied read-only collections and raw-response retention in `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/TradeLifecycleResults.cs`
- [X] T005 [P] Add `InternalsVisibleTo` access for `MetaTrader.Grpc.Client.Tests` without changing package targets or dependencies in `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/MetaTrader.Grpc.Client.csproj`
- [X] T006 Add failing tests for DONE, DONE_PARTIAL, PLACED, LOCKED, every documented rejection category, missing `TradeResult`, and unrecognized future retcodes in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.Tests/TradeExecutionClassifierTests.cs`
- [X] T007 Implement the operation-aware conservative raw-retcode table and exact raw-code preservation in `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/TradeExecutionClassifier.cs`
- [X] T008 [P] Add structured lifecycle-operation and batch-item status logging helpers that exclude credentials, comments, and full payloads in `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/Mt5GrpcClientLogging.cs`
- [X] T009 Create the internal send/position delegate seam, validation-failure result factory, protobuf cloning helpers, and single effective-deadline capture in `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/TradeLifecycleExecutor.cs`
- [X] T010 Retain the client logger and construct the production `TradeLifecycleExecutor` from unchanged `SendOrderAsync` and `GetPositionsAsync` delegates in `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/Mt5GrpcClient.cs`

**Checkpoint**: Shared lifecycle types and execution infrastructure compile for `netstandard2.0` and `net472`, classifier tests pass, and `SendOrderAsync` remains unchanged.

---

## Phase 3: User Story 1 - Open and Close a Position (Priority: P1) MVP

**Goal**: Open market or pending orders and fully or partially close positions through explicit operations that issue exactly one send and expose separate call and execution outcomes.

**Independent Test**: Submit one market open, one pending open, one ticket-only full close, one ticket/volume partial close, and one pending-order cancellation; verify action/field mapping, raw result retention, zero calls on invalid input, bounded documented lookups, exactly one send on valid input, and no retry.

### Tests for User Story 1

- [X] T011 [P] [US1] Add failing mapping, validation, caller-immutability, zero-call, bounded-lookup, exactly-one-send, and no-retry tests for market/pending open, full/partial position close, and pending-order cancellation in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.Tests/TradeLifecycleMappingTests.cs`
- [X] T012 [P] [US1] Add failing public-surface tests for `OpenOrderAsync`, scalar `ClosePositionAsync`, `CloseOrderAsync`, optional volume/deadline/cancellation parameters, and `TradeOperationResult` compatibility in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/TradeLifecycleSurfaceTests.cs`

### Implementation for User Story 1

- [X] T013 [US1] Implement market DEAL and pending PENDING validation/mapping, including order-type categories, finite values, stop-limit rules, time-policy/expiration rules, and cloned timestamps in `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/TradeLifecycleExecutor.cs`
- [X] T014 [US1] Implement full/partial position-close validation and opposite-side DEAL mapping from one position lookup plus one symbol-info lookup, and pending-order REMOVE mapping without lookup, in `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/TradeLifecycleExecutor.cs`
- [X] T015 [US1] Add `OpenOrderAsync`, scalar `ClosePositionAsync`, and `CloseOrderAsync` wrappers that forward one effective deadline/token, delegate at most one send, classify the response, and never retry in `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/Mt5GrpcClient.TradeLifecycle.cs`
- [X] T016 [US1] Run the US1 tests in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.Tests/MetaTrader.Grpc.Client.Tests.csproj` and `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/MetaTrader.Grpc.Client.ContractTests.csproj`

**Checkpoint**: User Story 1 is fully functional and independently testable as the MVP.

---

## Phase 4: User Story 2 - Modify an Existing Trade (Priority: P2)

**Goal**: Modify either position protection or pending-order final-state values through one explicit operation that selects SLTP or MODIFY automatically.

**Independent Test**: Modify one position's SL/TP and one pending order's price/time/expiration; verify exact target/action/value mapping, explicit zero-as-clear behavior, cloned timestamps, zero calls for ambiguous targets, and exactly one send otherwise.

### Tests for User Story 2

- [X] T017 [P] [US2] Add failing position/pending modification mapping, final-state value, expiration-cloning, ambiguous-target, invalid-number, zero-call, and exactly-one-send tests in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.Tests/TradeLifecycleMappingTests.cs`
- [X] T018 [P] [US2] Extend public-surface tests for `ModifyTradeAsync`, `ModifyTradeRequest`, `PositionModification`, `PendingOrderModification`, and returned operation identity in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/TradeLifecycleSurfaceTests.cs`

### Implementation for User Story 2

- [X] T019 [US2] Implement exactly-one-target validation plus SLTP position and MODIFY pending-order final-state mapping with no hidden lookup in `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/TradeLifecycleExecutor.cs`
- [X] T020 [US2] Add `ModifyTradeAsync` with input snapshotting, one send, shared error semantics, response classification, and no retry in `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/Mt5GrpcClient.TradeLifecycle.cs`
- [X] T021 [US2] Run the US2 tests in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.Tests/MetaTrader.Grpc.Client.Tests.csproj` and `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/MetaTrader.Grpc.Client.ContractTests.csproj`

**Checkpoint**: User Stories 1 and 2 both work and can be validated independently.

---

## Phase 5: User Story 3 - Close One Position by an Opposite Position (Priority: P3)

**Goal**: Submit one hedging close-by request with two positive distinct tickets in caller-specified roles and preserve MT5 rejection or execution details.

**Independent Test**: Submit two distinct opposite-position tickets and verify one CLOSE_BY send with unswapped `position`/`position_by`; verify invalid or identical tickets make zero calls and MT5 rejection remains a received but non-completed execution result.

### Tests for User Story 3

- [X] T022 [P] [US3] Add failing public-surface tests for `ClosePositionByAsync`, `CloseByRequest`, ticket role preservation, and optional magic/comment inputs in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/TradeLifecycleSurfaceTests.cs`
- [X] T023 [P] [US3] Add failing close-by mapping, positive/distinct-ticket validation, unswapped-role, exactly-one-send, no-lookup, no-retry, and MT5-rejection preservation tests in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.Tests/TradeLifecycleMappingTests.cs`

### Implementation for User Story 3

- [X] T024 [US3] Implement CLOSE_BY request validation/mapping with exact primary/opposite ticket roles and MT5-authoritative live-state checks in `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/TradeLifecycleExecutor.cs`
- [X] T025 [US3] Add `ClosePositionByAsync` with one-send execution, response classification, bounded logging, and no retry in `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/Mt5GrpcClient.TradeLifecycle.cs`
- [X] T026 [US3] Run the US3 tests in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.Tests/MetaTrader.Grpc.Client.Tests.csproj` and `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/MetaTrader.Grpc.Client.ContractTests.csproj`

**Checkpoint**: User Stories 1 through 3 work independently; the reusable single close-by behavior is ready for batching.

---

## Phase 6: User Story 4 - Process Multiple Close-By Pairs (Priority: P4)

**Goal**: Discover one symbol/magic scope, freeze eligible tickets, refresh frozen membership before each deterministic FIFO pairing decision, submit sequential close-by attempts without retry, and retain every attempted, unattempted, withheld, ineligible, missing, and unpaired outcome.

**Independent Test**: Script three eligible FIFO pairs, reject the second, and verify all three ordered outcomes; then verify magic filtering, tie-breaking, new-position exclusion, partial-volume re-pairing, failure withholding, refresh errors, cancellation/deadline stopping, and complete remainder accounting.

### Tests for User Story 4

- [X] T027 [P] [US4] Extend public-surface tests for `ClosePositionsByAsync`, `ClosePositionsByRequest`, immutable batch collections, pair indices/roles, statuses, errors, and remainder reasons in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/TradeLifecycleSurfaceTests.cs`
- [X] T028 [P] [US4] Add failing scripted tests for blank-symbol zero calls, symbol/magic discovery, frozen membership, new-position exclusion, FIFO/open-time ordering, ascending-ticket tie-breaking, BUY-primary roles, empty batches, and unmatched remainders in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.Tests/MultipleCloseByTests.cs`
- [X] T029 [US4] Extend scripted tests for second-pair rejection continuation, failed/accepted/unknown/transport-uncertain ticket withholding, no retry, partial-completion refresh/re-pairing, disappeared/ineligible tickets, and retained prior outcomes in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.Tests/MultipleCloseByTests.cs`
- [X] T030 [US4] Extend scripted tests for one captured explicit/default deadline, shared cancellation token, cancellation/deadline during refresh or send, discovery/refresh failure, zero later sends, and complete attempted/unattempted/remainder accounting in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.Tests/MultipleCloseByTests.cs`

### Implementation for User Story 4

- [X] T031 [US4] Implement initial symbol discovery, optional magic/eligibility filtering, deterministic frozen-ticket ordering, snapshot cloning, and empty-batch completion in `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/TradeLifecycleExecutor.cs`
- [X] T032 [US4] Implement per-decision symbol refresh/intersection, FIFO plus ticket sorting, sequential BUY-primary close-by submission, partial-volume reuse, independent-failure continuation, and failed/uncertain ticket withholding in `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/TradeLifecycleExecutor.cs`
- [X] T033 [US4] Implement discovery/refresh terminal statuses, cancellation/deadline stop checks, materialized unattempted pairs, deterministic remainder reasons/volumes, immutable result collections, and O(N) retained state in `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/TradeLifecycleExecutor.cs`
- [X] T034 [US4] Add `ClosePositionsByAsync` with one effective absolute deadline, one shared token, production discovery/send delegates, per-pair logging, and no rollback or retry in `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/Mt5GrpcClient.TradeLifecycle.cs`
- [X] T035 [US4] Run the US4 tests in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.Tests/MetaTrader.Grpc.Client.Tests.csproj` and `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/MetaTrader.Grpc.Client.ContractTests.csproj`

**Checkpoint**: All four stories are functional, deterministic, and independently testable without a live MT5 terminal or broker.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Complete documentation, examples, compatibility, observability, release metadata, and reproducible package verification across all stories.

- [X] T036 [P] Document all six methods, full versus partial position close, pending-order cancellation, final-state modification, hedging constraints, call versus execution status, batch inspection/non-atomic behavior, and no-retry warnings in `mt5_grpc_client_csharp/README.md`
- [X] T037 [P] Add independently runnable market/pending open, full/partial position close, pending-order cancellation, position/pending modification, single close-by, and batch result-inspection examples in `mt5_grpc_client_csharp/examples/NetStandardClientExample/Program.cs`
- [X] T038 [P] Add supported-surface examples for the six lifecycle methods in `mt5_grpc_client_csharp/examples/NetFramework48ClientExample/Program.cs`
- [X] T039 [P] Extend net48 compile/reference assertions for every new method, DTO, result, enum, and optional deadline/token signature in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.CompatibilityTests/NetFramework48ReferenceTests.cs`
- [X] T040 [P] Add documentation contract checks for all six method examples, execution-status inspection, hedging/non-atomic guidance, and uncertain-outcome no-retry warnings in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/DocumentationAccuracyTests.cs`
- [X] T041 [P] Add tests that lifecycle and batch logs contain operation/item/status identity but omit credentials, comment text, and complete trade payloads in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.Tests/Mt5GrpcLoggingTests.cs`
- [X] T042 [P] Bump the additive client version to 4.3.0 and update package release notes while preserving target frameworks, dependency groups, `ProtoContractIdentity`, and `TestedServerVersionRange` in `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/MetaTrader.Grpc.Client.csproj`
- [X] T043 [P] Add the 4.3.0 trade-lifecycle feature, compatibility statement, and no-proto/no-server impact to `mt5_grpc_client_csharp/CHANGELOG.md`
- [X] T044 Run the complete Release test suite for `mt5_grpc_client_csharp/MetaTrader.Grpc.Client.sln` and resolve feature-caused regressions without changing existing `SendOrderAsync` behavior
- [X] T045 Build both `mt5_grpc_client_csharp/examples/NetStandardClientExample/NetStandardClientExample.csproj` and `mt5_grpc_client_csharp/examples/NetFramework48ClientExample/NetFramework48ClientExample.csproj` in Release mode
- [X] T046 Run `mt5_grpc_client_csharp/scripts/check-generated.ps1` in Release mode and verify generated bindings still match unchanged `protos/trade.proto` and `protos/position.proto`
- [X] T047 Run `mt5_grpc_client_csharp/scripts/check-package-metadata.ps1` in Release mode and verify exactly the existing target/dependency groups plus the planned 4.3.0 metadata
- [X] T048 Run `mt5_grpc_client_csharp/scripts/verify-consumer-restore.ps1` with `-Configuration Release -ModernTfm net9.0` and verify clean modern and net48 consumers compile without protobuf generation
- [X] T049 Pack `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/MetaTrader.Grpc.Client.csproj` in Release with `ContinuousIntegrationBuild=true` and inspect the local `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/bin/Release/MetaTrader.Grpc.Client.4.3.0.nupkg` without publishing
- [X] T050 Execute every command and expected check in `specs/006-trade-lifecycle-operations/quickstart.md`, then review the final diff to confirm `protos/trade.proto`, `protos/position.proto`, generated bindings, Python packages, and server code are unchanged

---

## Phase 8: Ticket-Only Close API Revision

**Purpose**: Supersede the original snapshot-based close contract with a
ticket-driven position close and add first-class pending-order cancellation.

- [X] T051 Add failing scalar-signature contract tests for ticket/optional-volume `ClosePositionAsync` and ticket-only `CloseOrderAsync` in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/TradeLifecycleSurfaceTests.cs`
- [X] T052 Add failing validation, lookup, mapping, deadline/token, REMOVE, zero-call, and no-retry tests in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.Tests/TradeLifecycleMappingTests.cs`
- [X] T053 Replace `ClosePositionRequest`/`PositionSide` with a scalar close surface and implement position plus symbol-info derivation under one effective deadline in `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/`
- [X] T054 Implement `CloseOrderAsync` with positive-ticket validation and one REMOVE send without lookup or retry in `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/`
- [X] T055 Update feature specification, design, contract, data model, research, quickstart, README, changelog, and package release notes for the revised RPC behavior
- [X] T056 Update both examples plus public/compatibility/documentation tests for the six-method lifecycle surface
- [X] T057 Run focused unit, contract, compatibility, and example verification in Release mode
- [X] T058 Run the full Release solution tests and generated/package/consumer guards, then confirm no proto/generated/server changes

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: T001 precedes T002; both establish the unchanged baseline.
- **Foundational (Phase 2)**: Depends on Setup. T003, T004, T005, and T008 can run in parallel. T006 depends on T004 and T005; T007 follows T006. T009 depends on T003, T004, T005, T007, and T008. T010 follows T009 and blocks all user-story implementation.
- **User Stories (Phases 3-6)**: Depend on Foundational completion. US1, US2, and US3 have no functional dependency on each other and remain independently testable, though their changes to `TradeLifecycleExecutor.cs`, `Mt5GrpcClient.TradeLifecycle.cs`, and shared test files must be coordinated if developed concurrently. US4 depends on the reusable single close-by behavior from US3.
- **Polish (Phase 7)**: Depends on every user story selected for release. T036-T043 can run in parallel; T044-T050 run after the corresponding code, tests, docs, examples, and metadata are complete.

### User Story Dependencies

- **User Story 1 (P1)**: Starts after T010; no dependency on another story. Tests T011-T012 precede implementation T013-T015; T016 validates the complete increment.
- **User Story 2 (P2)**: Starts after T010; no dependency on US1. Tests T017-T018 precede T019-T020; T021 validates the complete increment.
- **User Story 3 (P3)**: Starts after T010; no dependency on US1 or US2. Tests T022-T023 precede T024-T025; T026 validates the complete increment.
- **User Story 4 (P4)**: Starts after T026 because it reuses single close-by semantics. Tests T027-T030 precede T031-T034; T035 validates the complete increment.

### Within Each User Story

- Write tests first and confirm they fail because the intended surface or behavior is absent.
- Implement validation and request mapping before public client wrappers.
- Preserve the original `Mt5GrpcResult<OrderSendResponse>` and raw protobuf response before deriving execution status.
- Validate the story independently at its checkpoint before proceeding to the next priority.

### Parallel Opportunities

- T003, T004, T005, and T008 can proceed together after Setup.
- T011 and T012 can proceed together for US1.
- T017 and T018 can proceed together for US2.
- T022 and T023 can proceed together for US3.
- T027 and T028 can proceed together for US4; T029 and T030 then extend the same scripted test file sequentially.
- T036-T043 can proceed together after all selected stories are complete.

---

## Parallel Examples

### User Story 1

```text
Task T011: Add open/close mapping and behavioral tests in TradeLifecycleMappingTests.cs
Task T012: Add open/close public contract tests in TradeLifecycleSurfaceTests.cs
```

### User Story 2

```text
Task T017: Add modification mapping and validation tests in TradeLifecycleMappingTests.cs
Task T018: Add modification public contract tests in TradeLifecycleSurfaceTests.cs
```

### User Story 3

```text
Task T022: Add close-by public contract tests in TradeLifecycleSurfaceTests.cs
Task T023: Add close-by mapping and behavioral tests in TradeLifecycleMappingTests.cs
```

### User Story 4

```text
Task T027: Add batch public contract tests in TradeLifecycleSurfaceTests.cs
Task T028: Add batch discovery and deterministic-pairing tests in MultipleCloseByTests.cs
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational.
3. Complete Phase 3: User Story 1.
4. Stop and validate T016 independently.
5. Demonstrate explicit market/pending open and full/partial close with separate call and execution outcomes.

### Incremental Delivery

1. Setup + Foundational establish the shared safe execution model.
2. US1 delivers the minimum open/close lifecycle MVP.
3. US2 adds risk-management modification without changing US1.
4. US3 adds auditable single close-by behavior.
5. US4 composes the tested single close-by behavior into deterministic non-atomic batching.
6. Polish completes examples, compatibility, and package verification for the additive 4.3.0 release.

### Parallel Team Strategy

1. Complete Setup and the shared foundational phase together.
2. After T010, separate contributors may prepare US1, US2, and US3 tests in parallel.
3. Coordinate edits to shared executor, client partial, and accumulated test files before merging each independently validated story.
4. Begin US4 after single close-by semantics pass T026.
5. Run documentation, examples, compatibility, logging, and release-metadata tasks in parallel before the sequential release gates.

---

## Notes

- `[P]` tasks touch different files and have no dependency on incomplete tasks in their phase.
- `[US1]` through `[US4]` provide requirement-to-story traceability.
- Tests use pure builders and scripted delegates; no live MT5 terminal or broker account is required.
- Do not edit `.proto`, generated binding, Python, or server files for this feature.
- Do not retry any trade submission whose execution might be uncertain.
- Do not publish a package from this workflow.
