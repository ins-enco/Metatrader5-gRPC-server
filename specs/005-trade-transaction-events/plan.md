# Implementation Plan: Trade Transaction Event Streaming

**Branch**: `005-trade-transaction-events` | **Date**: 2026-07-07 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/005-trade-transaction-events/spec.md`

## Summary

Add a real-time **trade transaction event** capability across the whole MT5 gRPC
stack — the shared proto contract, the Python server, and the C# client — where
none exists today. The capability is delivered as the **first streaming RPC** in
the contract: a new `TradeEventsService.SubscribeTradeTransactions` **server-streaming**
RPC that emits one `TradeTransactionEvent` per newly added MT5 deal as it is
observed on the connected account.

Because the MetaTrader 5 Python API exposes **no push/event callback**
(`OnTradeTransaction` exists only in MQL5/EA), "real-time" is emulated by
**server-side polling of `history_deals_get`** over an advancing time window.
Each subscription tracks a per-stream watermark (last delivered `time_msc` +
deal ticket) so it delivers every qualifying deal **exactly once, in
chronological order, with no duplicates**, even when deals share a millisecond.
The subscription starts at "now" by default (no historical replay); an explicit
past start time backfills once, capped to a **7-day lookback**. Poll cadence
defaults to **1000 ms**, clamped to a server floor of **200 ms**.

The C# client exposes the stream as an `IAsyncEnumerable<TradeTransactionEvent>`
mapping 1:1 to the server stream (primary surface), with a thin `event`-style
wrapper layered on top (convenience surface), following the library's existing
`Mt5GrpcResult`/`Mt5GrpcError` conventions. All existing unary RPCs, messages,
and field numbers are **unchanged**; this is a purely additive, backward-compatible
contract change.

## Technical Context

**Language/Version**: Python 3.8+ (server + proto package; runtime pinned to 3.11 per `.pyc` cache); C# `netstandard2.0` (client library)
**Primary Dependencies**: Server/proto — `grpcio>=1.68.1`, `grpcio-tools>=1.68.1`, `protobuf>=5.29.2`, `MetaTrader5`. Client — `Google.Protobuf` 3.29.3, `Grpc.Core.Api`/`Grpc.Net.Client` 2.71.0, `Grpc.Tools` 2.71.0 (`PrivateAssets="all"`), `Microsoft.Extensions.Logging.Abstractions` 9.0.0; **NEW**: `Microsoft.Bcl.AsyncInterfaces` (provides `IAsyncEnumerable<T>` on `netstandard2.0`)
**Storage**: N/A — subscription watermark is in-memory per stream; source of truth is MT5 `history_deals_get`
**Testing**: `pytest` (Python server: unit + a mock-MT5 streaming/poll-loop test that runs without a live broker); `xUnit` (C# Tests / ContractTests / CompatibilityTests) — the streaming-client generation pattern is already proven by `StreamingFixtureService`
**Target Platform**: Windows-first server (MetaTrader 5 terminal; documented Wine workflow); C# package consumed on .NET Framework 4.8 → modern .NET
**Project Type**: Multi-language repo — canonical protos in `protos/`, generated Python bindings in `mt5_grpc_proto/`, Python server in `mt5_grpc_server/`, C# client in `mt5_grpc_client_csharp/`
**Performance Goals**: Event observed within one poll interval (default ≤1000 ms) under normal conditions (SC-001); each poll issues one bounded `history_deals_get` window query; back-pressure via gRPC flow control rather than dropping events
**Constraints**: Additive contract only — no existing RPC/message/field-number change (FR-012); poll floor 200 ms (protect terminal); 7-day backfill cap; transport-level keepalive only (no app heartbeat, FR-014); no secret/credential leakage in stream logs; **bounded server worker usage** — a long-lived streaming RPC occupies a `ThreadPoolExecutor` worker for the life of the stream (current pool = 10)
**Scale/Scope**: One new `.proto` file (1 service, 1 streaming RPC, 1 request message, 1 event message); one new Python servicer + registration; one new C# client partial + streaming invoker + event wrapper; regenerate Python + C# bindings; new tests + docs. Multiple concurrent independent subscriptions supported (FR-010, SC-007).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Protocol contract**: **PASS**. The change adds a new `protos/trade_events.proto`
  (package `metatrader.v1`) with one new service, one server-streaming RPC, one
  request message, and one event message; it imports the existing `common.proto`
  `Error` type. **No existing `.proto` file, RPC, message, field, field number, or
  enum is added-to, renamed, removed, or renumbered.** New-file field numbers start
  at 1 and are documented as frozen. Bindings are regenerated from source for both
  languages (`generate_proto.sh`; C# `Grpc.Tools` on build). Generated code is not
  hand-edited. See [research.md](./research.md) Decisions 1, 5 and
  [contracts/trade_events.proto](./contracts/trade_events.proto).
- **MT5 behavior fidelity**: **PASS**. The event is mapped to MT5's
  `history_deals_get` (the same call the existing `TradeHistoryService.GetDeals`
  uses) polled over an advancing window; "trade transaction" = newly added deal
  (emulating MQL5 `OnTradeTransaction` `DEAL_ADD`). Deal ticket/order/position are
  64-bit MT5 tickets; `type`/`entry` carry verbatim MT5 numeric semantics (mirroring
  the existing `Deal` message's `int32 type`/`int32 entry`); time is MT5
  `time_msc` (ms). Failures (terminal not initialized, lookup failure) surface via
  `mt5.last_error()` mapped into the shared `Error` type — identical to existing
  services (FR-009). See [research.md](./research.md) Decisions 2, 3, 6 and
  [data-model.md](./data-model.md).
- **Multi-language type safety**: **PASS**. Contract avoids Python-only concepts:
  time and cadence are explicit millisecond `int64`; tickets are `uint64`;
  price/volume/profit are `double`; direction/entry are `int32` with documented MT5
  meaning; optional request fields use proto3 `optional` with documented sentinel
  ("unset/0 `from_time_msc` = start now"). In-band `Error` reuses the shared type.
  No Python class names or ambiguous scalars cross the boundary. See
  [data-model.md](./data-model.md) *Cross-Language Type Notes*.
- **Reliability and observability**: **PASS**. The plan defines: cancellation
  (poll loop checks `context.is_active()` / cancellation and exits promptly,
  releasing its worker — SC-004, FR-008); failure modes (uninitialized terminal,
  transient vs. persistent lookup failure → in-band `Error` frame then stream end);
  exactly-once/ordering via a `(time_msc, ticket)` watermark tolerant of
  same-millisecond ties (FR-006); back-pressure via gRPC flow control (no event
  drop); bounded per-poll work (single windowed query); keepalive delegated to
  gRPC/HTTP-2 PING (FR-014); and **worker-pool sizing** so concurrent long-lived
  streams do not starve unary calls. Logging follows the existing interceptor
  pattern without emitting credentials. See [research.md](./research.md)
  Decisions 3, 4, 7.
- **Test and release reproducibility**: **PASS**. Generation is reproducible
  (`generate_proto.sh` for Python; `Grpc.Tools` regenerates C# on build, guarded by
  `check-generated.ps1`). A `pytest` test drives the poll loop against a **mock MT5**
  (no live broker) asserting exactly-once/ordering, default-start-is-now, backfill
  cap, and cadence clamping. C# `ContractTests` assert the generated streaming client
  surface (as `StreamingFixtureTests` already do for the fixture) and the
  `IAsyncEnumerable` + event wrapper behavior against an in-process test server.
  Package versions for `mt5_grpc_proto`, `mt5_grpc_server`, and
  `MetaTrader.Grpc.Client` are bumped together as a coordinated backward-compatible
  minor release, and the C# `ProtoContractIdentity`/`TestedServerVersionRange`
  metadata is updated. See [quickstart.md](./quickstart.md).

**Result**: All five gates PASS. No entries required in Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/005-trade-transaction-events/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
│   └── trade_events.proto
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created here)
```

### Source Code (repository root)

```text
protos/
└── trade_events.proto                     # NEW: TradeEventsService + messages (canonical contract)

mt5_grpc_proto/mt5_grpc_proto/
├── trade_events_pb2.py                    # NEW (generated): messages
├── trade_events_pb2_grpc.py               # NEW (generated): service stubs
└── __init__.py                            # EDIT: export trade_events_pb2 / _grpc

mt5_grpc_server/mt5_grpc_server/
├── imp/
│   ├── trade_events.py                    # NEW: TradeEventsServiceImpl (poll loop + watermark)
│   └── __init__.py                        # EDIT: `from .trade_events import *`
└── grpc_server.py                         # EDIT: register TradeEventsServiceServicer; size worker pool
mt5_grpc_server/tests/
└── test_trade_events_stream.py            # NEW: mock-MT5 poll-loop / exactly-once / clamp tests

mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/
├── Mt5GrpcClient.cs                       # EDIT: expose TradeEventsService client
├── Mt5GrpcClient.TradeEvents.cs           # NEW: SubscribeTradeTransactions IAsyncEnumerable + event wrapper
├── Mt5GrpcStreamingInvoker.cs             # NEW: streaming counterpart to Mt5GrpcUnaryInvoker
└── MetaTrader.Grpc.Client.csproj          # EDIT: add Microsoft.Bcl.AsyncInterfaces; bump Version + metadata
mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/
├── ProtoContractCatalog.cs                # EDIT: register TradeEventsService (streaming) entry
└── TradeEventsStreamingTests.cs           # NEW: surface + IAsyncEnumerable + event wrapper tests
```

**Structure Decision**: This is the repository's established multi-language
layout — canonical protos in `protos/`, generated Python in `mt5_grpc_proto/`,
server servicers under `mt5_grpc_server/mt5_grpc_server/imp/` (one file per
service, aggregated by `imp/__init__.py` and registered in `grpc_server.py`), and
the C# client as partial classes over a shared `Mt5GrpcClient` with generated
clients built from `protos/*.proto`. The feature slots directly into these
patterns: a new proto file, a new servicer file + registration, and a new client
partial + a streaming invoker mirroring the existing `Mt5GrpcUnaryInvoker`.

## Complexity Tracking

> No Constitution Check violations. This section intentionally left empty.
