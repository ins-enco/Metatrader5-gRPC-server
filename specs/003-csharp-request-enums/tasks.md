---
description: "Task list for C# Request Enum Types (native proto enums)"
---

# Tasks: C# Request Enum Types (native proto enums)

**Input**: Design documents from `/specs/003-csharp-request-enums/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/proto-request-enums.md, quickstart.md

**Tests**: Included. This feature changes a public protobuf contract, regenerates
bindings, adds a server validation rule, and updates documented examples — all
categories the template requires tests for.

**Organization**: Tasks are grouped by user story. The contract change and binding
regeneration are the coordinated foundation (Phase 2) that every story depends on.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Exact file paths are included in every task

## Path Conventions

Multi-language repo (per plan.md): shared `protos/`, Python bindings in
`mt5_grpc_proto/`, Python server in `mt5_grpc_server/`, C# client in
`mt5_grpc_client_csharp/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish a clean, buildable baseline before touching the contract.

- [X] T001 Confirm baseline builds green before changes: run `dotnet build mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/MetaTrader.Grpc.Client.csproj` and the Python proto generation (`mt5_grpc_proto/generate_proto.sh`) so the pre-change state is known-good and regressions are attributable
- [X] T002 [P] Cross-check the authoritative MT5 values in `specs/003-csharp-request-enums/Mt5Enums.cs` against the mapping table in `specs/003-csharp-request-enums/data-model.md` (all four enums), recording any discrepancy before it is encoded into the contract (FR-009)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The coordinated `.proto` contract change plus binding regeneration and
the version/contract-identity bump. Every user story below consumes the generated
enum types, so this phase MUST complete first.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T003 [P] Add `enum ENUM_ORDER_TYPE` (9 members, `ORDER_TYPE_BUY=0` … `ORDER_TYPE_CLOSE_BY=8`) to `protos/common.proto`, values verbatim per `specs/003-csharp-request-enums/contracts/proto-request-enums.md` (FR-005, FR-015)
- [X] T004 [P] In `protos/trade.proto`, add `enum ENUM_TRADE_REQUEST_ACTIONS` (with `TRADE_ACTION_UNSPECIFIED=0` sentinel + MT5 values 1,5,6,7,8,10), `enum ENUM_ORDER_TYPE_FILLING` (0–2), `enum ENUM_ORDER_TYPE_TIME` (0–3); then retype `TradeRequest.action` (field 1), `type` (11), `type_filling` (12), `type_time` (13) from `int32` to their enum types, preserving field numbers (FR-006, FR-009, FR-012, data-model.md)
- [X] T005 [P] In `protos/order_calc.proto`, retype `OrderCalcMarginRequest.action` (field 1) and `OrderCalcProfitRequest.action` (field 1) from `int32` to `ENUM_ORDER_TYPE`, preserving field numbers; `common.proto` is already imported (FR-005, FR-015)
- [X] T006 Regenerate the Python bindings by running `mt5_grpc_proto/generate_proto.sh` and confirm the updated `common_pb2.py`, `trade_pb2.py`, `order_calc_pb2.py` reflect the new enums (depends on T003, T004, T005) (FR-013)
- [X] T007 Regenerate the C# bindings by running `dotnet build mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/MetaTrader.Grpc.Client.csproj`; confirm `Metatrader.V1` now exposes the four enum types and the retyped properties, and that the build fails on any remaining internal integer assignment (depends on T003, T004, T005) (FR-011, FR-012, FR-013)
- [X] T008 Bump `<Version>` from `0.1.0` to `0.2.0` and update `<ProtoContractIdentity>` (e.g. `protos-003-csharp-request-enums`) in `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/MetaTrader.Grpc.Client.csproj`; verify `<TestedServerVersionRange>` still covers the server (FR-016)

**Checkpoint**: Contract updated, both binding sets regenerated, package versioned — user stories can now proceed.

---

## Phase 3: User Story 1 - Build a Trade Request With Named Values (Priority: P1) 🎯 MVP

**Goal**: A C# developer sets the trade request's action, type, filling, and
time-in-force fields from named MT5 values with compile-time safety, and the server
rejects an unset/UNSPECIFIED action instead of executing it.

**Independent Test**: Construct a `TradeRequest` using only named values (no numeric
literal); the transmitted values equal the documented MT5 numbers, and a request
with an unset action returns a structured error placing no order.

### Tests for User Story 1 ⚠️

> Write these tests FIRST and ensure they FAIL before implementation.

- [X] T009 [P] [US1] Create `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.Tests/RequestEnumTests.cs` asserting value fidelity for `ENUM_TRADE_REQUEST_ACTIONS`, `ENUM_ORDER_TYPE`, `ENUM_ORDER_TYPE_FILLING`, `ENUM_ORDER_TYPE_TIME` — `(int)member` equals the MT5 value in data-model.md, and a set→serialize→parse→read round-trip preserves each (FR-003, SC-002)
- [X] T010 [P] [US1] Add an unknown-value round-trip test in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.Tests/RequestEnumTests.cs`: set `TradeRequest.Action = (ENUM_TRADE_REQUEST_ACTIONS)99`, serialize/parse/read → `(int)` equals 99 with no throw (proto3 open enum) (FR-007, FR-008, SC-005)
- [X] T011 [P] [US1] Extend the generated-surface / contract catalog checks in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/GeneratedSurfaceTests.cs` (and `ProtoContractCatalog.cs`) to assert `TradeRequest.Action/Type/TypeFilling/TypeTime` are the enum types on field numbers 1/11/12/13 (FR-013, SC-008)
- [X] T012 [P] [US1] Add a Python server test in `mt5_grpc_server/tests/test_trade_action_validation.py` asserting a trade request with unset action and one with `TRADE_ACTION_UNSPECIFIED (0)` return a structured error and place no order (FR-014, SC-009)

### Implementation for User Story 1

- [X] T013 [US1] In `mt5_grpc_server/mt5_grpc_server/imp/trade.py`, reject a trade request whose `action` is unset/`0` (`TRADE_ACTION_UNSPECIFIED`) with a structured `Error` and no order placed, before the existing action handling at line ~18 (FR-014, SC-009)
- [X] T014 [US1] Verify `mt5_grpc_server/mt5_grpc_server/imp/order_check.py` reads the retyped `action/type/type_filling/type_time` as identical integer values to the prior `int32` contract; adjust only if regeneration changed access semantics (SC-008)
- [X] T015 [P] [US1] Update the trade-request example in `mt5_grpc_client_csharp/examples/NetStandardClientExample/Program.cs` to use named values (`ENUM_TRADE_REQUEST_ACTIONS.TRADE_ACTION_DEAL`, `ENUM_ORDER_TYPE.ORDER_TYPE_BUY`, filling, time) per quickstart.md (FR-010, SC-006)

**Checkpoint**: Trade requests build with named values, cross-field/raw-int misuse fails to compile, and the server rejects UNSPECIFIED actions — MVP is independently testable.

---

## Phase 4: User Story 2 - Choose Buy/Sell for Calculation Requests (Priority: P2)

**Goal**: A C# developer sets `OrderCalcMarginRequest.Action` and
`OrderCalcProfitRequest.Action` using the same shared `ENUM_ORDER_TYPE` named
values used for trade submission.

**Independent Test**: Build a margin request and a profit request selecting the
direction by name; each carries the documented MT5 order-type numeric value and
uses the identical `ENUM_ORDER_TYPE` type as `TradeRequest.Type`.

### Tests for User Story 2 ⚠️

- [X] T016 [P] [US2] In `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.Tests/RequestEnumTests.cs`, assert shared-identity: `OrderCalcMarginRequest.Action`, `OrderCalcProfitRequest.Action`, and `TradeRequest.Type` are all `ENUM_ORDER_TYPE`, and each transmits the documented value (FR-005, FR-015, SC-002)
- [X] T017 [P] [US2] Extend `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/GeneratedSurfaceTests.cs` to assert both calc `action` fields are `ENUM_ORDER_TYPE` on field number 1 (FR-013, SC-008)

### Implementation for User Story 2

- [X] T018 [US2] Verify `mt5_grpc_server/mt5_grpc_server/imp/order_calc.py` reads the retyped `action` on both margin and profit requests as identical integer values to the prior `int32` contract; adjust only if regeneration changed access semantics (SC-008)
- [X] T019 [P] [US2] Update the margin and profit examples in `mt5_grpc_client_csharp/examples/NetStandardClientExample/Program.cs` to use `ENUM_ORDER_TYPE` named values, documenting inline that profit calc expects Buy/Sell (not compile-enforced), per quickstart.md (FR-005, FR-010, SC-006)

**Checkpoint**: Calculation requests use the shared named order-type set; US1 and US2 both work independently.

---

## Phase 5: User Story 3 - Migrate Existing Integer-Based Code (Priority: P3)

**Goal**: A developer upgrading from `0.1.x` sees invalid integer assignments flagged
by the compiler and converts each to a named value (or explicit cast) using a
documented migration path, with zero change to transmitted numeric values.

**Independent Test**: A `0.1.x` integer-based sample, upgraded per the migration
guide, compiles against `0.2.0` and transmits the same numeric values as before.

### Tests for User Story 3 ⚠️

- [X] T020 [P] [US3] Extend the compatibility suite in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.CompatibilityTests/` with a net48/netstandard2.0 test that builds requests using the named values and asserts identical behavior/values to a modern target (FR-011, SC-007)
- [X] T021 [P] [US3] Extend `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/DocumentationAccuracyTests.cs` to verify the migration examples (integer→named value pairs) transmit identical numeric values (SC-004)

### Implementation for User Story 3

- [X] T022 [P] [US3] Create `mt5_grpc_client_csharp/MIGRATION.md` documenting the integer→enum path for all six covered fields: each prior integer assignment mapped to its named value (identical transmitted value), plus the `(EnumType)value` cast for values with no named member (FR-006, FR-016, SC-004)
- [X] T023 [P] [US3] Add a `0.2.0` breaking-change entry to `mt5_grpc_client_csharp/CHANGELOG.md` describing the six retyped fields, the wire-compatible/source-breaking nature, and a link to MIGRATION.md (FR-016)
- [X] T024 [P] [US3] Extend `mt5_grpc_client_csharp/README.md` with a named-value usage section and a pointer to MIGRATION.md (FR-010)
- [X] T025 [P] [US3] Update the .NET Framework 4.8 example `mt5_grpc_client_csharp/examples/NetFramework48ClientExample/Program.cs` to use the named values, confirming parity on the compatibility target (FR-011, SC-006, SC-007)

**Checkpoint**: Migration is documented, versioned, and verified on all supported target frameworks; all three stories are independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final verification across stories and quickstart validation.

- [X] T026 [P] Verify the regenerated Python bindings in `mt5_grpc_proto/mt5_grpc_proto/` match `protos/` (no stale generated files; enums present in `common_pb2.py`, `trade_pb2.py`, `order_calc_pb2.py`) (FR-013)
- [X] T027 [P] Run the full C# test suite (`Tests`, `ContractTests`, `CompatibilityTests`) and the Python server tests; confirm all pass (SC-001 – SC-009)
- [X] T028 Run the `specs/003-csharp-request-enums/quickstart.md` build/test commands end-to-end to validate the documented developer flow (SC-006)
- [X] T029 [P] Confirm the does-not-compile snippet (raw int and wrong-enum assignment to `TradeRequest.Action`) from contracts/proto-request-enums.md is accurate and referenced in quickstart.md, since cross-field safety (SC-003) is verified by documentation rather than an in-suite test (research.md Decision 6)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories. Within it, T003/T004/T005 (different proto files) are parallel; T006/T007 (regeneration) depend on all three; T008 (version) is independent of regeneration but part of the coordinated bump.
- **User Stories (Phase 3–5)**: All depend on Foundational completion. Once done, US1/US2/US3 can proceed in parallel or in priority order (P1 → P2 → P3).
- **Polish (Phase 6)**: Depends on all targeted user stories being complete.

### User Story Dependencies

- **US1 (P1)**: Depends only on Foundational. Delivers the MVP (trade named values + server rejection).
- **US2 (P2)**: Depends only on Foundational. Shares `ENUM_ORDER_TYPE` with US1 but is independently testable.
- **US3 (P3)**: Depends only on Foundational. Migration docs/tests reference the retyped fields but do not depend on US1/US2 implementation.

### Within Each User Story

- Tests are written first and must FAIL before implementation.
- Server rejection (T013) before/independent of example updates.
- Story complete before moving to the next priority.

### Parallel Opportunities

- Setup: T002 runs alongside T001's build wait.
- Foundational: T003, T004, T005 (three different proto files) in parallel.
- US1 tests: T009, T010, T011, T012 in parallel (different files).
- US2 tests: T016, T017 in parallel.
- US3: T020, T021, T022, T023, T024, T025 are largely parallel (distinct files).
- Polish: T026, T027, T029 in parallel.
- With staff, US1/US2/US3 can be developed concurrently after Phase 2.

---

## Parallel Example: Foundational Contract Changes

```bash
# Three independent proto files — edit together:
Task: "Add ENUM_ORDER_TYPE to protos/common.proto"
Task: "Add trade enums + retype fields in protos/trade.proto"
Task: "Retype OrderCalc action fields in protos/order_calc.proto"
# Then regenerate (barrier): Python bindings, C# bindings.
```

## Parallel Example: User Story 1 Tests

```bash
Task: "Value fidelity + round-trip tests in RequestEnumTests.cs"
Task: "Unknown-value round-trip test in RequestEnumTests.cs"
Task: "Generated-surface assertions in GeneratedSurfaceTests.cs"
Task: "Server UNSPECIFIED-rejection test in test_trade_action_validation.py"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational (contract + regeneration + version) — CRITICAL, blocks all stories.
3. Complete Phase 3: User Story 1 (trade named values + server rejection).
4. **STOP and VALIDATE**: Build a trade request with named values, confirm compile-time safety and UNSPECIFIED rejection.
5. Ship/demo if ready.

### Incremental Delivery

1. Setup + Foundational → contract and bindings ready.
2. US1 → test → MVP.
3. US2 → test → calculation requests share the named order-type set.
4. US3 → test → migration documented and compatibility verified.
5. Polish → full-suite + quickstart validation.

### Parallel Team Strategy

After Phase 2, one developer takes US1 (server + trade tests/examples), one takes
US2 (calc verification + examples), one takes US3 (migration docs + compatibility
tests) — the stories touch mostly distinct files.

---

## Notes

- [P] tasks = different files, no dependencies.
- [Story] label maps each task to its user story for traceability.
- Verify tests fail before implementing.
- The contract change is wire-compatible (varint) but source-breaking for C# integer callers — coordinated via `0.2.0` + MIGRATION.md.
- Cross-field compile-time safety (SC-003) is inherent to native enums and verified by documentation (research.md Decision 6), not an in-suite test.
- Commit after each task or logical group.
