---
description: "Task list for Trade Transaction Event Streaming implementation"
---

# Tasks: Trade Transaction Event Streaming

**Input**: Design documents from `/specs/005-trade-transaction-events/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/trade_events.proto, quickstart.md

**Tests**: Included. This feature changes public protobuf contracts, generated bindings, a
new server adapter, the C# client surface, and packaging — all categories that require tests
per the template. The Python poll loop is tested against a **mock** `history_deals_get` (no
live broker); C# streaming is tested against the generated surface / in-process stream.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Exact file paths are included in every task

## Path Conventions

Multi-language repo (per plan.md):
- Canonical protos: `protos/`
- Generated Python bindings: `mt5_grpc_proto/mt5_grpc_proto/`
- Python server: `mt5_grpc_server/mt5_grpc_server/` (servicers under `imp/`), tests in `mt5_grpc_server/tests/`
- C# client: `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/`, contract tests in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Bring the canonical contract into the repo and add the one new client dependency needed to compile the streaming surface.

- [x] T001 Copy the designed contract into the canonical proto directory: `cp specs/005-trade-transaction-events/contracts/trade_events.proto protos/trade_events.proto` (verify it `import "common.proto";`, package `metatrader.v1`, field numbers frozen starting at 1)
- [x] T002 [P] Add `<PackageReference Include="Microsoft.Bcl.AsyncInterfaces" Version="9.0.0" />` to `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/MetaTrader.Grpc.Client.csproj` so `IAsyncEnumerable<T>` is available on `netstandard2.0` (do NOT change target framework)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Regenerate bindings for both languages and stand up the servicer + client wiring skeleton that ALL user stories build on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [x] T003 Regenerate Python gRPC bindings by running `./generate_proto.sh` from repo root (produces `mt5_grpc_proto/mt5_grpc_proto/trade_events_pb2.py` and `trade_events_pb2_grpc.py`; do not hand-edit generated files)
- [x] T004 Export the new module from `mt5_grpc_proto/mt5_grpc_proto/__init__.py` by adding `from .trade_events_pb2 import *` and `from .trade_events_pb2_grpc import *`
- [x] T005 [P] Regenerate and verify C# bindings by running `pwsh mt5_grpc_client_csharp/scripts/check-generated.ps1` (confirm a `TradeEventsService.TradeEventsServiceClient` with a `ServerStream`-style `SubscribeTradeTransactions` is emitted, per the `StreamingFixtureService` pattern)
- [x] T006 Create servicer skeleton `TradeEventsServiceImpl(trade_events_pb2_grpc.TradeEventsServiceServicer)` in `mt5_grpc_server/mt5_grpc_server/imp/trade_events.py` with a `SubscribeTradeTransactions(self, request, context)` generator stub (body filled per-story) and imports of `MetaTrader5 as mt5`, the pb2 modules, and the shared `common_pb2.Error`
- [x] T007 Add `from .trade_events import *` to `mt5_grpc_server/mt5_grpc_server/imp/__init__.py`
- [x] T008 Register the servicer and size the worker pool in `mt5_grpc_server/mt5_grpc_server/grpc_server.py`: import `trade_events_pb2_grpc`, call `trade_events_pb2_grpc.add_TradeEventsServiceServicer_to_server(TradeEventsServiceImpl(), server)`, and raise `ThreadPoolExecutor(max_workers=...)` from 10 to 32 (budget for concurrent long-lived streams + unary headroom, per research Decision 4) with an explanatory comment
- [x] T009 [P] Expose the generated client on the shared client: add a `TradeEventsService.TradeEventsServiceClient TradeEvents` property in `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/Mt5GrpcClient.cs` (mirroring how existing service clients are exposed)
- [x] T010 Create `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/Mt5GrpcStreamingInvoker.cs` — a streaming counterpart to `Mt5GrpcUnaryInvoker` that wraps an `AsyncServerStreamingCall`, maps faults through `Mt5GrpcError`/`Mt5GrpcErrorMapper`, and logs stream faults like the unary path (no method body per-stream yet; provides the shared invoke helper)

**Checkpoint**: Contract exists in both languages, server registers an (empty) streaming RPC, client can reach it — user stories can now begin.

---

## Phase 3: User Story 1 - Subscribe to live trade transactions (Priority: P1) 🎯 MVP

**Goal**: A subscriber receives exactly one ordered `TradeTransactionEvent` per newly added deal on the connected account as it occurs, with no duplicates (even for same-millisecond deals), consumable as an `IAsyncEnumerable` and via an `event` wrapper.

**Independent Test**: Start a subscription, execute/close a trade on the account, confirm the subscriber receives one event describing that deal within one poll interval; execute several deals in quick succession and confirm one event each, in chronological order, no duplicates.

### Tests for User Story 1 ⚠️ (write first, ensure they FAIL before implementation)

- [x] T011 [P] [US1] Python test in `mt5_grpc_server/tests/test_trade_events_stream.py`: feed a mock `history_deals_get` a sequence of 100+ deals across bursts including same-millisecond `time_msc` ties; assert each is delivered exactly once, in `(time_msc, ticket)` ascending order, zero duplicates/omissions (SC-003, FR-006)
- [x] T012 [P] [US1] Python test in `mt5_grpc_server/tests/test_trade_events_stream.py`: when the terminal is not initialized or `history_deals_get` returns `None` (persistent failure), the stream emits one final `TradeTransactionEvent` with `error` populated from `mt5.last_error()` and then ends (FR-009, edge cases)
- [x] T013 [P] [US1] Python test in `mt5_grpc_server/tests/test_trade_events_stream.py`: with no new deals, the subscription yields no events and stays open/healthy across multiple poll cycles (US1 acceptance #3)
- [x] T014 [P] [US1] C# test in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/TradeEventsStreamingTests.cs`: `await foreach` over `SubscribeTradeTransactionsAsync(...)` yields typed `TradeTransactionEvent` items 1:1 with an in-process/fake server stream, and the generated streaming surface exists (as `StreamingFixtureTests` asserts for the fixture)

### Implementation for User Story 1

- [x] T015 [US1] In `mt5_grpc_server/mt5_grpc_server/imp/trade_events.py`, implement the poll loop with a `(time_msc, ticket)` watermark: each iteration query `history_deals_get` over a clock-skew-widened window around `[watermark_second, now]` (Decision 8), sort candidates by `(time_msc, ticket)` ascending, skip any `(time_msc, ticket) <= watermark`, emit the remainder, advance the watermark (exactly-once + ordering, FR-006, Decision 3)
- [x] T016 [US1] In `mt5_grpc_server/mt5_grpc_server/imp/trade_events.py`, map each MT5 deal to a `TradeTransactionEvent` (deal_ticket, order→order_ticket, position_id→position_ticket, symbol, volume, price, profit, time_msc, verbatim `type`, verbatim `entry`) per data-model.md (FR-002, FR-003)
- [x] T017 [US1] In `mt5_grpc_server/mt5_grpc_server/imp/trade_events.py`, resolve poll cadence (unset → 1000 ms; `< 200` → clamp up to 200 ms) and `sleep(cadence)` between polls (FR-007)
- [x] T018 [US1] In `mt5_grpc_server/mt5_grpc_server/imp/trade_events.py`, emit a terminal in-band `Error` frame from `mt5.last_error()` on terminal/persistent lookup failure (retry a transient single-poll failure within the cadence before escalating), then end the stream (FR-009, Decision 6)
- [x] T019 [US1] Implement the core `IAsyncEnumerable<TradeTransactionEvent> SubscribeTradeTransactionsAsync(SubscribeTradeTransactionsRequest request, DateTime? deadline, CancellationToken ct)` in `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/Mt5GrpcClient.TradeEvents.cs`, wrapping the generated `AsyncServerStreamingCall` via `Mt5GrpcStreamingInvoker` and `yield return`-ing each message (1:1 with the server stream; primary surface, FR-011)
- [x] T020 [US1] Add the `event`-style convenience wrapper in `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/Mt5GrpcClient.TradeEvents.cs` — a subscription helper exposing `event EventHandler<TradeTransactionEvent>` that drives the async sequence on a background task, following the library's `Mt5GrpcResult`/`Mt5GrpcError` conventions (convenience surface over the sequence, FR-011)
- [x] T021 [US1] Register a `TradeEventsService` (streaming) entry in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/ProtoContractCatalog.cs` so the generated-surface contract tests cover the new service
- [x] T022 [US1] Ensure bounded logging without secrets/credentials on the streaming path in `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/Mt5GrpcStreamingInvoker.cs` (mirror the unary invoker's logging discipline)

**Checkpoint**: Live trade transactions stream end-to-end, exactly-once and ordered, over both the `IAsyncEnumerable` and `event` surfaces. This is the MVP.

---

## Phase 4: User Story 2 - Start without replaying stale history (Priority: P1)

**Goal**: A subscription starts at "now" by default (no historical replay), or backfills once from an explicit past start time bounded to a 7-day lookback.

**Independent Test**: On an account with existing historical deals, start with no start time and confirm zero historical deals delivered; start with an explicit past start time and confirm deals from that point forward are delivered once, in order, then live deals continue.

### Tests for User Story 2 ⚠️ (write first, ensure they FAIL before implementation)

- [x] T023 [P] [US2] Python test in `mt5_grpc_server/tests/test_trade_events_stream.py`: with default (unset/0) `from_time_msc` on an account that already has historical deals, zero historical deals are delivered — only deals after subscription start (SC-002, FR-005)
- [x] T024 [P] [US2] Python test in `mt5_grpc_server/tests/test_trade_events_stream.py`: with an explicit past `from_time_msc`, deals from that time forward are backfilled once in `(time_msc, ticket)` order, followed by live deals with no gap/duplicate (US2 acceptance #2)
- [x] T025 [P] [US2] Python test in `mt5_grpc_server/tests/test_trade_events_stream.py`: a `from_time_msc` older than `now − 7 days` is clamped forward to the 7-day cap (FR-004)

### Implementation for User Story 2

- [x] T026 [US2] In `mt5_grpc_server/mt5_grpc_server/imp/trade_events.py`, implement start-point resolution at stream open — explicit past `from_time_msc` ⇒ `max(from_time_msc, now_ms − 7*24*3600*1000)` initializing the watermark to `(start, UINT64_MAX)`; unset/0 ⇒ baseline the watermark on the newest existing deal at the first poll (server-time base, no replay) rather than the host clock (FR-004, FR-005, Decision 5, Decision 8)

**Checkpoint**: US1 + US2 both work independently — live streaming with a controlled, bounded start point.

---

## Phase 5: User Story 3 - Clean shutdown and resubscription (Priority: P2)

**Goal**: A subscriber can cancel at any time with prompt resource release, and can resume after a disconnect from the last received transaction's timestamp with no gap and no duplicate.

**Independent Test**: Start a subscription, cancel it, confirm the loop exits promptly and no further events arrive; then start a new subscription from the last received transaction's `time_msc` and confirm continuity with no gap or duplicate of the boundary transaction.

### Tests for User Story 3 ⚠️ (write first, ensure they FAIL before implementation)

- [x] T027 [P] [US3] Python test in `mt5_grpc_server/tests/test_trade_events_stream.py`: simulating `context.is_active()` becoming false (client cancel/disconnect) stops the poll loop within one cadence and performs no further `history_deals_get` calls (SC-004, FR-008)
- [x] T028 [P] [US3] Python test in `mt5_grpc_server/tests/test_trade_events_stream.py`: resuming a new subscription with `from_time_msc` = the last received transaction's `time_msc` re-fetches the boundary second but the `(time_msc, ticket)` filter drops the already-seen deal — no gap, no duplicate (US3 acceptance #2, Decision 3)
- [x] T029 [P] [US3] C# test in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/TradeEventsStreamingTests.cs`: cancelling the `CancellationToken` flows through call options and ends `await foreach`; the `event` wrapper signals completion on normal end and surfaces a mapped `Mt5GrpcError` on fault so a consumer can resubscribe

### Implementation for User Story 3

- [x] T030 [US3] In `mt5_grpc_server/mt5_grpc_server/imp/trade_events.py`, add cooperative cancellation — check `context.is_active()` (and catch cancellation) each loop iteration and exit promptly, releasing the executor worker (FR-008, SC-004)
- [x] T031 [US3] In `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/Mt5GrpcClient.TradeEvents.cs`, add completion/error signalling to the `event` wrapper (a completion callback plus a mapped-error callback) so consumers can detect stream end/fault and resubscribe from the last received `time_msc` (FR-014, User Story 3)

**Checkpoint**: All three user stories are independently functional — live streaming, bounded start, and clean cancel/resume.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Coordinated packaging, regression verification, and docs.

- [x] T032 Coordinated backward-compatible **minor** version bump for `mt5_grpc_proto`, `mt5_grpc_server`, and `MetaTrader.Grpc.Client` (use `bump_version.py` where applicable); update each `CHANGELOG.md`
- [x] T033 Refresh C# package metadata in `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/MetaTrader.Grpc.Client.csproj`: `ProtoContractIdentity`, `TestedServerVersionRange`, and `PackageReleaseNotes` for the new streaming service
- [x] T034 [P] Verify generated bindings match sources with no drift by running `pwsh mt5_grpc_client_csharp/scripts/check-generated.ps1` (C#) and re-running `./generate_proto.sh` (Python) and confirming a clean git diff on generated files
- [x] T035 Run full regression suites and confirm zero regressions (SC-005, FR-012): `python -m pytest mt5_grpc_server/tests -v` and `dotnet test mt5_grpc_client_csharp/MetaTrader.Grpc.Client.sln`
- [x] T036 [P] Update documentation with a `SubscribeTradeTransactions` usage example (`await foreach` + `event` wrapper) and note the additive/backward-compatible contract change; keep examples in sync with `DocumentationAccuracyTests`
- [x] T037 Run the quickstart.md verification path end-to-end and confirm the Definition-of-Done traceability table (SC-001…SC-007) is satisfied

---

## Phase 7: Post-Release Hotfix — broker server-time base (Decision 8)

**Purpose**: Fix a defect found during live testing — on a broker whose server time
is offset from the server host's UTC clock (e.g. EET, UTC+2/+3), a just-created deal
fell outside the UTC-tight poll window and was never delivered (SC-001 broken), and
the host-clock "start now" watermark could drop new deals or replay recent history
(SC-002 at risk). Root cause: MT5 `history_deals_get`/`deal.time_msc` operate in the
broker server-time base, but the poll window and default watermark were derived from
`time.time()` (host UTC). No wire/contract change — server runtime only.

- [x] T038 [P] Python regression tests in `mt5_grpc_server/tests/test_trade_events_stream.py`: (a) default start-now delivers a new deal whose `time_msc` is offset ahead **and** behind the host clock (parametrized), and (b) default start-now baselines an account's existing history without replay, then delivers a subsequently-appearing deal (SC-001, SC-002, Decision 8)
- [x] T039 In `mt5_grpc_server/mt5_grpc_server/imp/trade_events.py`, add `CLOCK_SKEW_MARGIN_MS` and (a) widen the poll window by that margin on both bounds and (b) for default start-now, defer the watermark to a first-poll baseline on the newest existing deal (server-time base) instead of seeding from `now_ms` (FR-005, Decision 8). Confirm all prior trade-events tests remain green.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup. **Blocks all user stories.** (T003→T004 sequential; T005/T009 [P]; T006→T007→T008 sequential; T010 after T005.)
- **User Stories (Phases 3–5)**: All depend on Foundational completion.
  - US1 (P1) and US2 (P1) can be developed in parallel by different developers, but both edit `imp/trade_events.py` — coordinate or serialize edits to that file.
  - US3 (P2) can start after Foundational; its resume test (T028) is most meaningful once US1's watermark (T015) exists.
- **Polish (Phase 6)**: Depends on all targeted user stories being complete.

### User Story Dependencies

- **US1 (P1)**: Independent — the core streaming path. Recommended first (MVP).
- **US2 (P1)**: Independent — adds start-point resolution; testable on its own.
- **US3 (P2)**: Independent — cancellation + resume; resume continuity relies on the watermark scheme shared with US1.

### Within Each User Story

- Tests written first and failing before implementation.
- Server poll/watermark (T015) before mapping/cadence/error refinements build on it.
- C# core `IAsyncEnumerable` (T019) before the `event` wrapper (T020, T031).

### Parallel Opportunities

- Setup: T002 is [P] with T001.
- Foundational: T005 and T009 are [P]; the Python chain (T003→T004, T006→T007→T008) runs alongside the C# chain.
- Every test task within a story marked [P] is a different-file test and can run together.
- With staff: US1 and US2 in parallel (mind the shared `imp/trade_events.py`), US3 alongside once Foundational is done.

---

## Parallel Example: User Story 1

```bash
# Launch all US1 tests together (all fail first):
Task: "Exactly-once/order + same-ms ties test in mt5_grpc_server/tests/test_trade_events_stream.py"
Task: "In-band Error on failure test in mt5_grpc_server/tests/test_trade_events_stream.py"
Task: "No-new-deals stays-open test in mt5_grpc_server/tests/test_trade_events_stream.py"
Task: "await foreach yields typed events test in .../ContractTests/TradeEventsStreamingTests.cs"
```

> Note: the three Python US1 tests target the same test file — write them as separate test
> functions; they are logically parallel but land in one file, so coordinate the edit.

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Complete Phase 1: Setup (contract in place, client dependency added).
2. Complete Phase 2: Foundational (bindings regenerated, servicer registered, client wired). **Blocks everything.**
3. Complete Phase 3: User Story 1 (core live streaming, both surfaces).
4. **STOP and VALIDATE**: exercise live delivery, exactly-once, ordering, in-band error.
5. Deploy/demo — this is a usable event capability.

### Incremental Delivery

1. Setup + Foundational → foundation ready.
2. US1 → live streaming MVP → validate → demo.
3. US2 → controlled/bounded start point → validate → demo.
4. US3 → clean cancel + resume → validate → demo.
5. Polish → coordinated version bump, regression green, docs, quickstart traceability.

### Parallel Team Strategy

1. Whole team completes Setup + Foundational together.
2. Then split: Dev A on US1 (core loop + C# core surface), Dev B on US2 (start resolution + tests), Dev C on US3 (cancellation + event-wrapper signalling). Serialize edits to `imp/trade_events.py` and `Mt5GrpcClient.TradeEvents.cs`.
3. Reconvene for Phase 6 packaging/regression.

---

## Notes

- [P] = different files, no dependency on an incomplete task.
- [Story] label maps each task to a user story for traceability.
- All existing unary RPCs, messages, and field numbers remain unchanged (FR-012, SC-005) — this is purely additive.
- Generated files (`*_pb2.py`, `*_pb2_grpc.py`, C# generated clients) are never hand-edited.
- Verify each story's tests fail before implementing; commit after each task or logical group.
