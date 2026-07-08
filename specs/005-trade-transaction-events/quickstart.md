# Quickstart: Trade Transaction Event Streaming

This is the build/verify path for the feature. It exercises the required
generation, unit, contract, integration, packaging, and documentation checks
(Constitution V). Commands assume the repo root and the project's existing tooling
(no new build system).

## 0. Prerequisites

- Python 3.11 env with `grpcio`, `grpcio-tools`, `protobuf`, `pytest`, and (on the
  server host) `MetaTrader5`. Tests use a **mock** `history_deals_get`, so a live
  broker is **not** required for CI.
- .NET SDK able to build `netstandard2.0` + run the xUnit test projects.

## 1. Add the contract (canonical source)

Copy the designed contract into the canonical proto directory:

```bash
cp specs/005-trade-transaction-events/contracts/trade_events.proto protos/trade_events.proto
```

## 2. Regenerate bindings

Python:

```bash
./generate_proto.sh
# Produces mt5_grpc_proto/mt5_grpc_proto/trade_events_pb2.py and _pb2_grpc.py
```

Then export the module from `mt5_grpc_proto/mt5_grpc_proto/__init__.py`:

```python
from .trade_events_pb2 import *
from .trade_events_pb2_grpc import *
```

C# (regenerates on build via `Grpc.Tools`; guarded by the drift check):

```powershell
pwsh mt5_grpc_client_csharp/scripts/check-generated.ps1
```

## 3. Implement the server

- `mt5_grpc_server/mt5_grpc_server/imp/trade_events.py` — `TradeEventsServiceImpl`
  with `SubscribeTradeTransactions(request, context)` implemented as a generator:
  resolve start (explicit past ⇒ 7-day-capped watermark; `now` ⇒ first-poll baseline
  on the newest existing deal, in the server-time base — Decision 8) and cadence
  (default 1000, floor 200), then loop `poll (clock-skew-widened window) → yield
  ordered new events → advance (time_msc,ticket) watermark → sleep(cadence)`, exiting
  when `context.is_active()` is false. Populate an in-band `Error` from
  `mt5.last_error()` on terminal/persistent failure, then end.
- `imp/__init__.py` — add `from .trade_events import *`.
- `grpc_server.py` — register
  `trade_events_pb2_grpc.add_TradeEventsServiceServicer_to_server(TradeEventsServiceImpl(), server)`
  and raise `ThreadPoolExecutor(max_workers=...)` to budget for concurrent
  long-lived streams (see plan Decision 4).

## 4. Implement the C# client

- `MetaTrader.Grpc.Client.csproj` — add
  `<PackageReference Include="Microsoft.Bcl.AsyncInterfaces" Version="…" />`, bump
  `<Version>`, and refresh `ProtoContractIdentity` / `TestedServerVersionRange` /
  `PackageReleaseNotes`.
- `Mt5GrpcClient.cs` — expose
  `TradeEventsService.TradeEventsServiceClient TradeEvents`.
- `Mt5GrpcStreamingInvoker.cs` + `Mt5GrpcClient.TradeEvents.cs` —
  `IAsyncEnumerable<TradeTransactionEvent> SubscribeTradeTransactionsAsync(...)`
  (core, 1:1 with the server stream) plus an `event`-style wrapper over it, with
  faults mapped through `Mt5GrpcError` and logged like the unary path.

## 5. Tests

Python:

```bash
python -m pytest mt5_grpc_server/tests/test_trade_events_stream.py -v
```

Assert (against mock `history_deals_get`):
- default start = now delivers **zero** historical deals (SC-002);
- explicit past start backfills once, in order (US2 #2);
- 100+ deals across bursts delivered **exactly once**, in `(time_msc,ticket)` order,
  incl. same-millisecond ties (SC-003, FR-006);
- cadence below 200 ms is clamped to 200; unset defaults to 1000 (FR-007);
- start older than 7 days is clamped forward (FR-004);
- cancellation stops the loop promptly (SC-004);
- terminal-not-initialized / lookup failure yields an in-band `Error` event (FR-009).

C#:

```powershell
dotnet test mt5_grpc_client_csharp/MetaTrader.Grpc.Client.sln
```

Assert: generated streaming client surface exists (as `StreamingFixtureTests` does
for the fixture); `await foreach` yields typed events; the event wrapper raises per
event and signals completion/error; cancellation flows through call options. Add a
`TradeEventsService` entry to `ProtoContractCatalog` (streaming).

## 6. Regression + packaging

- Existing Python and C# suites pass unchanged (SC-005, FR-012):
  ```bash
  python -m pytest mt5_grpc_server/tests -v
  dotnet test mt5_grpc_client_csharp/MetaTrader.Grpc.Client.sln
  ```
- Coordinated backward-compatible **minor** version bump for `mt5_grpc_proto`,
  `mt5_grpc_server`, and `MetaTrader.Grpc.Client` (use `bump_version.py` where
  applicable); update `CHANGELOG.md` files.

## 7. Definition of done (traceability)

| Success criterion | Verified by |
|-------------------|-------------|
| SC-001 event within one cadence | integration/mock timing test |
| SC-002 no historical replay by default | `test_trade_events_stream` default-start case |
| SC-003 exactly-once/order over 100+ | `test_trade_events_stream` burst + ties case |
| SC-004 prompt cancel + resource release | cancellation test + worker-pool sizing |
| SC-005 zero regressions | full existing suites green |
| SC-006 generated client talks to server | C# surface test + in-process/live smoke |
| SC-007 concurrent subscriptions | multi-subscription test, independent watermarks |
