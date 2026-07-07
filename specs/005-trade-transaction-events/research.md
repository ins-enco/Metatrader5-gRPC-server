# Phase 0 Research: Trade Transaction Event Streaming

All decisions below were resolvable from the (already clarified) spec and the
existing codebase; there are **no open `NEEDS CLARIFICATION` items**. Each entry
records the decision, why it was chosen, and the alternatives rejected.

---

## Decision 1 — Contract shape: new file, new service, first streaming RPC

**Decision**: Add a new canonical proto file `protos/trade_events.proto` in package
`metatrader.v1` (C# namespace `Metatrader.V1`, matching every existing proto).
It declares:
- `service TradeEventsService` with one **server-streaming** RPC
  `rpc SubscribeTradeTransactions(SubscribeTradeTransactionsRequest) returns (stream TradeTransactionEvent);`
- `message SubscribeTradeTransactionsRequest` (optional start time, optional cadence)
- `message TradeTransactionEvent` (FR-003 fields + optional shared `Error`)

It `import "common.proto";` to reuse the shared `Error` type. Field numbers start
at 1 and are documented as frozen.

**Rationale**: A new file with new symbols is a strictly additive,
backward-compatible change (Constitution I, FR-012): no existing message, field,
or field number is touched, so consumers on the current contract are unaffected
until they adopt the service. A dedicated `TradeEventsService` (rather than adding
a streaming method to `TradeHistoryService`) keeps the first streaming RPC's
concerns isolated and leaves the unary history service's contract identity intact.

**Alternatives rejected**:
- *Add `stream` RPC to existing `TradeHistoryService` (`deal.proto`)* — mutates a
  file whose surface is asserted by `ProtoContractCatalog`/contract tests and
  couples the streaming lifecycle to the unary history service; larger blast radius
  for no benefit.
- *Reuse the existing `Deal` message as the stream item* — `Deal` carries fields
  beyond FR-003 (commission, swap, fee, magic, reason, comment, external_id) and
  has no place for an in-band `Error`. A purpose-built event message keeps the
  contract minimal and self-describing per FR-003.

---

## Decision 2 — MT5 mapping: emulate events by polling `history_deals_get`

**Decision**: Derive events from **newly added deals** using
`mt5.history_deals_get(date_from, date_to)` over an advancing time window, exactly
as `TradeHistoryServiceImpl.GetDeals` already calls it. Each poll queries
`[watermark_time, now]`, filters out already-delivered deals, emits the rest in
order, then advances the watermark.

**Rationale**: The MetaTrader 5 **Python** API has no push/event callback —
`OnTradeTransaction` is an MQL5/EA-only concept. Polling the historical deals feed
is the only MT5-observable way to detect completed trade actions from Python, and
it reuses a call path already validated in this codebase (Constitution II — MT5
fidelity). A "deal added" is the closest Python-observable equivalent of a
completed trade action (matches the spec's Assumptions).

**Alternatives rejected**:
- *Wait for a native MT5 Python event* — does not exist; would never fire.
- *Poll positions/orders state diffs* — reconstructs deals indirectly, is harder to
  de-duplicate, and drifts from the spec's explicit "detection source is deals"
  assumption (order/position event types are out of scope this version).

---

## Decision 3 — Exactly-once + ordering via a `(time_msc, ticket)` watermark

**Decision**: Track per-subscription state as the last delivered
`(time_msc, deal_ticket)`. Each poll:
1. queries `history_deals_get(from=floor(watermark_time_msc/1000), to=now)` (MT5
   time filters are second-granular, so query from the watermark's **second** to
   avoid missing sub-second-later deals),
2. sorts candidates by `(time_msc, ticket)` ascending,
3. skips any deal with `(time_msc, ticket) <= watermark`,
4. emits the remainder, advancing the watermark to the last emitted
   `(time_msc, ticket)`.

Deduplication is keyed on the **deal ticket** (globally unique, monotonic), not on
timestamp, so two deals sharing a millisecond are both delivered once (FR-006, edge
case "clock/timestamp ambiguity").

**Rationale**: Ticket is the only reliably unique, stable identifier; time alone is
not (ties happen). Querying from the watermark's second and then filtering by the
precise `(time_msc, ticket)` tuple guarantees no gap (second-granular query is a
superset) and no duplicate (tuple filter removes the re-fetched boundary deals).
This also makes **resume** correct: a client restarting from the last received
`time_msc` (User Story 3, FR resume) re-fetches the boundary second but the tuple
filter drops the already-seen deal.

**Alternatives rejected**:
- *Timestamp-only watermark* — loses or duplicates same-millisecond deals.
- *Deliver everything since `date_from` each poll and dedupe on the client* —
  violates exactly-once-per-subscription server contract and wastes bandwidth.

---

## Decision 4 — Poll loop, cancellation, and worker-pool sizing

**Decision**: Implement the RPC as a generator that loops: poll → `yield` events →
`sleep(cadence)`, checking `context.is_active()` (and catching cancellation) each
iteration so a cancelled/disconnected client ends the loop **promptly** and frees
its executor worker (FR-008, SC-004). Because a server-streaming RPC on a
`grpc.server(ThreadPoolExecutor(...))` **occupies one worker for the life of the
stream**, raise the pool from the current `max_workers=10` to a size that budgets
for expected concurrent subscriptions plus unary headroom (e.g. `max_workers=32`,
final value fixed during implementation), and document it.

**Rationale**: The existing synchronous `grpc` server dispatches each RPC on a pool
thread; long-lived streams that never return would otherwise consume the whole pool
and starve unary calls — a direct Constitution IV (reliability/resource-bounds)
concern that must be addressed for FR-010/SC-007 (multiple concurrent
subscriptions). Cooperative cancellation via `context.is_active()` is the
idiomatic sync-gRPC pattern and needs no extra threads.

**Alternatives rejected**:
- *Rewrite the server on `grpc.aio` (async)* — would remove the per-stream-thread
  cost but is a large, cross-cutting change to a synchronous server and every
  existing servicer; out of scope and disproportionate for one feature.
- *Leave `max_workers=10`* — caps concurrent streams at ~10 minus in-flight unary
  calls and risks starvation; unacceptable for a streaming feature.

---

## Decision 5 — Start point, backfill cap, and cadence clamping semantics

**Decision**: `SubscribeTradeTransactionsRequest` carries `optional int64
from_time_msc` and `optional int64 poll_interval_ms`. Server rules:
- **Unset or `0`** `from_time_msc` ⇒ start at current server time (no historical
  replay) — FR-005.
- Explicit past `from_time_msc` older than `now - 7 days` ⇒ **clamp forward** to
  `now - 7 days` (bounded one-shot backfill) — FR-004.
- `poll_interval_ms` unset ⇒ **1000 ms**; any value `< 200` ⇒ **clamp up to 200 ms**
  — FR-007.

**Rationale**: Millisecond `int64` for both fields is language-neutral and
preserves ordering precision (Constitution III); the proto3 `optional`/zero
sentinel convention ("unset/0 = now") matches how the rest of the contract encodes
optional inputs. Clamping (rather than erroring) matches the clarified spec and
protects the terminal from excessive querying and start-up cost.

**Alternatives rejected**:
- *Reject sub-floor cadence / >7-day start with an error* — spec explicitly chose
  clamping for a forgiving client experience.
- *Encode start time as `google.protobuf.Timestamp`* — adds a well-known-type
  dependency and second/nanos split; a flat ms `int64` is simpler and consistent
  with the existing `Deal.time_msc` field.

---

## Decision 6 — In-band error reporting mirrors existing services

**Decision**: `TradeTransactionEvent` includes `optional Error error = <n>;`. On a
recoverable/terminal failure (terminal not initialized, `history_deals_get`
returns `None`, persistent lookup failure) the server emits a final event whose
`error` is populated from `mt5.last_error()` (same mapping used by
`deals_history.py`) and then ends the stream; transient single-poll failures may be
retried within the cadence before escalating. Invalid requests and broken
transport surface through the standard gRPC status/error path the C# client already
maps.

**Rationale**: Reusing the shared `common.proto` `Error` and the existing
`mt5.last_error()` mapping keeps consumer error handling identical across unary and
streaming calls (FR-009, Constitution II/IV). The C# `Mt5GrpcErrorMapper` already
translates both in-band `Error` payloads and `RpcException`s, so the streaming path
reuses it.

**Alternatives rejected**:
- *A separate error stream / status-only signalling* — inconsistent with the rest
  of the API and harder for consumers to correlate.
- *Silent stream end on failure* — violates "structured error instead of silent
  hang" (spec edge cases, Constitution II).

---

## Decision 7 — C# surface: `IAsyncEnumerable` core + `event` wrapper on `netstandard2.0`

**Decision**: Add `Mt5GrpcClient.TradeEvents.cs` exposing:
- **Core**: `IAsyncEnumerable<TradeTransactionEvent> SubscribeTradeTransactionsAsync(request, deadline, CancellationToken)`
  that wraps the generated `AsyncServerStreamingCall` and yields each message
  (`await foreach`-able, 1:1 with the server stream).
- **Wrapper**: a subscription helper exposing a C# `event
  EventHandler<TradeTransactionEvent>` (plus an error/completion signal) that
  internally drives the async sequence on a background task until cancelled.

Add a new `Mt5GrpcStreamingInvoker` mirroring `Mt5GrpcUnaryInvoker` for consistent
error mapping (`Mt5GrpcError`) and logging on stream faults. Add the
**`Microsoft.Bcl.AsyncInterfaces`** package so `IAsyncEnumerable<T>` is available on
`netstandard2.0` (`LangVersion` is already `latest`, enabling `await foreach` /
async iterators).

**Rationale**: The clarified spec requires **both** surfaces, with the async
sequence primary and the event a convenience over it. `netstandard2.0` does **not**
include `IAsyncEnumerable<T>` (it ships in .NET Standard 2.1 / the BCL async
interfaces package), so the package reference is mandatory — this is a packaging
consistency item (Constitution project constraints), handled by a coordinated
version bump. The streaming client type is already known to generate from a
`stream` RPC — the repo's `StreamingFixtureService` fixture and
`StreamingFixtureTests` prove `Grpc.Tools` emits `ServerStream`-style clients and
that cancellation options flow through.

**Alternatives rejected**:
- *Event-only or sequence-only* — contradicts the clarified requirement (FR-011).
- *Bump the client to `netstandard2.1`/multi-target* — would drop or complicate .NET
  Framework 4.8 support that the package explicitly targets; adding the async
  interfaces package keeps the single `netstandard2.0` target.
- *Return `Mt5GrpcResult<IAsyncEnumerable<…>>`* — the result/error envelope suits
  one-shot unary calls; for a stream, per-message yield with faults surfaced via the
  streaming invoker (mapped `Mt5GrpcError` on the terminal exception / in-band
  `Error`) is the natural shape.

---

## Cross-cutting notes

- **Generation workflow**: Python bindings via `generate_proto.sh`
  (`grpc_tools.protoc` over `protos/*.proto`) then export the new module from
  `mt5_grpc_proto/__init__.py`; C# bindings regenerate automatically on build
  (`Grpc.Tools`, `GrpcServices="Client"`), guarded by `check-generated.ps1`. No new
  build system is introduced (spec Assumption).
- **Versioning**: coordinated **minor** bump for `mt5_grpc_proto`,
  `mt5_grpc_server`, and `MetaTrader.Grpc.Client` (backward-compatible addition),
  plus refreshed C# `ProtoContractIdentity` / `TestedServerVersionRange` /
  `PackageReleaseNotes`.
- **Testability without a broker**: the poll loop is written against an injected /
  patchable `history_deals_get` so `pytest` can feed synthetic deal sequences
  (bursts, same-ms ties, empty polls, failures) and assert exactly-once, ordering,
  default-start-now, 7-day clamp, and cadence clamp; C# tests assert generated
  surface + `await foreach` + event wrapper against an in-process/fake stream.
