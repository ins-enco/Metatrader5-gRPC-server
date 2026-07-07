# Feature Specification: Trade Transaction Event Streaming

**Feature Branch**: `005-trade-transaction-events`
**Created**: 2026-07-07
**Status**: Draft
**Input**: User description: "TradeTransaction event streaming across the MT5 gRPC stack — add a real-time trade action / trade transaction event capability that currently does not exist in the proto contract, the Python server, or the C# client."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Subscribe to live trade transactions (Priority: P1)

An application developer using the MetaTrader gRPC client library wants to be notified whenever a
new trade transaction (a deal being added to the account's trade history) occurs on the connected
MetaTrader 5 account, so their application can react in near-real-time (update a dashboard, trigger a
notification, record an audit trail) without repeatedly querying the account themselves.

**Why this priority**: This is the core value of the feature. Without it there is no event capability
at all. Every other story builds on the ability to receive a live stream of transactions.

**Independent Test**: Start a subscription against an account, place or close a trade on that account
through any means, and confirm the subscriber receives one event describing that transaction shortly
after it occurs. Delivers standalone value: continuous awareness of account activity.

**Acceptance Scenarios**:

1. **Given** an active subscription started at time T, **When** a new deal is executed on the account
   after T, **Then** the subscriber receives exactly one event describing that deal (ticket, order,
   position, symbol, volume, price, profit, timestamp, direction/entry).
2. **Given** an active subscription, **When** multiple deals occur in quick succession, **Then** the
   subscriber receives one event per deal, in chronological order, with no duplicates.
3. **Given** an active subscription, **When** no new deals occur, **Then** the subscriber receives no
   events and the subscription remains open and healthy.

---

### User Story 2 - Start without replaying stale history (Priority: P1)

A developer starting a subscription wants to control the point in time from which events begin, so a
freshly started application is not flooded with the account's entire historical deal log.

**Why this priority**: Without a defined starting point, a new subscription on an account with a long
history would replay thousands of old transactions, making the feature unusable in practice. This is
part of the minimum viable behavior.

**Independent Test**: On an account with existing historical deals, start a subscription with a
starting time of "now" and confirm no historical deals are delivered — only deals occurring after the
subscription started.

**Acceptance Scenarios**:

1. **Given** an account with pre-existing historical deals, **When** a subscription is started with no
   explicit starting time, **Then** only deals occurring after the subscription start are delivered.
2. **Given** a subscription started with an explicit starting time in the past, **When** the stream
   begins, **Then** deals from that starting time forward are delivered once, in order, and live deals
   continue after them.

---

### User Story 3 - Clean shutdown and resubscription (Priority: P2)

A developer wants to stop listening at any time and, after a disconnect, resume from where they left
off without missing or duplicating transactions.

**Why this priority**: Long-lived streams will be cancelled and connections will drop in real
deployments. Predictable cancellation and a documented way to resume make the feature dependable, but
the feature is still demonstrable without it.

**Independent Test**: Start a subscription, cancel it, confirm resources are released promptly and no
further events arrive; then start a new subscription using the last received transaction's timestamp
and confirm continuity with no gap or duplication.

**Acceptance Scenarios**:

1. **Given** an active subscription, **When** the subscriber cancels it, **Then** the stream ends
   promptly and the server stops producing events for that subscription.
2. **Given** a subscription that ended (cancelled or connection lost) after receiving a transaction at
   time L, **When** the subscriber starts a new subscription from time L, **Then** it receives every
   transaction after L with no gap and no duplicate of the transaction at L.

---

### Edge Cases

- **No account / not initialized**: If the underlying MT5 terminal is not initialized or logged in
  when a subscription starts, the subscriber receives a clear, structured error consistent with the
  library's existing error reporting rather than a silent hang.
- **MT5 query failure mid-stream**: If a transaction lookup fails transiently while a subscription is
  open, the stream surfaces the error in the same shape as other operations; a persistent failure ends
  the stream with a descriptive error rather than emitting incorrect data.
- **Clock / timestamp ambiguity**: Two deals sharing the same millisecond timestamp must both be
  delivered exactly once (ordering and de-duplication cannot rely on timestamp uniqueness alone).
- **Multiple concurrent subscribers**: Several independent subscriptions to the same account each
  receive the full set of transactions relevant to their own starting point, without interfering with
  one another.
- **Very high transaction rate**: A burst of transactions is delivered without dropping events; if the
  subscriber consumes slowly, the stream applies back-pressure rather than losing data.
- **Existing unary calls unaffected**: Introducing the streaming capability does not change the
  behavior, request/response shapes, or field numbering of any existing operation.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST allow a client to open a long-lived subscription that delivers trade
  transaction events for the connected MetaTrader 5 account as they occur.
- **FR-002**: The system MUST derive trade transaction events from newly added deals in the account's
  trade history (the closest MT5-observable equivalent of a completed trade action).
- **FR-003**: Each delivered event MUST include, at minimum: deal ticket, associated order ticket,
  associated position ticket, symbol, executed volume, price, profit, transaction time, deal direction
  (buy/sell), and deal entry type (in/out/inout).
- **FR-004**: The system MUST allow the client to specify the point in time from which events should
  begin, so historical transactions before that point are not delivered.
- **FR-005**: When the client does not specify a starting time, the system MUST begin from the moment
  the subscription starts (no historical replay by default).
- **FR-006**: The system MUST deliver each qualifying transaction exactly once per subscription, in
  chronological order, with no duplicates, including when multiple transactions share the same
  timestamp.
- **FR-007**: The system MUST allow the client to control how frequently the account is checked for new
  transactions (poll cadence), within safe bounds enforced by the server.
- **FR-008**: The system MUST end a subscription promptly and release its resources when the client
  cancels it or the connection is lost.
- **FR-009**: The system MUST report failures (terminal not initialized, transaction lookup failure,
  invalid request) using the same structured error model as existing operations, so consumers handle
  them consistently.
- **FR-010**: The system MUST support multiple concurrent, independent subscriptions without one
  affecting another's delivery.
- **FR-011**: The consuming library MUST expose the subscription in a way idiomatic to its existing
  surface, allowing consumers to receive events as an asynchronous sequence and/or via an event
  callback, following the library's established result and error conventions.
- **FR-012**: The addition MUST be backward compatible — no existing operation's contract, behavior, or
  field numbering may change.
- **FR-013**: The capability MUST be available consistently across the shared contract, the server, and
  the client library so that a client generated from the contract can talk to the server without manual
  adjustment.

### Protocol and MT5 Contract Impact *(mandatory)*

- **Affected RPCs/Messages**:
  - New protocol service (e.g. `TradeEventsService`) with one new **server-streaming** RPC (e.g.
    `SubscribeTradeTransactions`) — this is the first streaming RPC in the contract; all existing RPCs
    remain unary and unchanged.
  - New request message carrying an optional starting time and an optional poll cadence.
  - New event message (`TradeTransactionEvent`) carrying the fields listed in FR-003, plus the shared
    `Error` type from `common.proto` for in-band error reporting consistent with other responses.
  - No changes to existing messages, fields, or field numbers.
- **Compatibility Decision**: Backward-compatible addition. New service and messages only; no
  modification to existing contracts. Consumers on the current contract are unaffected until they adopt
  the new service.
- **MT5 Operation Mapping**: Backed by the MetaTrader 5 historical deals query
  (`history_deals_get`) polled over a time window. There is no push/event callback in the MetaTrader 5
  Python API (`OnTradeTransaction` exists only in MQL5/EA), so the "event" is emulated by detecting
  deals added since the last observed point. Timestamps use MT5 deal time in milliseconds; identifiers
  (deal/order/position tickets) carry MT5 semantics. Error codes/messages follow the existing
  `mt5.last_error()` mapping used by other services.
- **Cross-Language Type Notes**: Starting time is expressed as a millisecond timestamp to preserve
  ordering precision across languages; poll cadence is expressed in milliseconds. Ticket identifiers
  use 64-bit integers. Monetary/price/volume values use double precision, consistent with existing
  messages. Optional request fields follow the proto3 optional/sentinel conventions already used in
  the contract (e.g. an unset or zero starting time is interpreted as "start now").

### Key Entities *(include if feature involves data)*

- **Trade Transaction Event**: Represents a single completed trade action observed on the account,
  derived from a deal. Key attributes: deal ticket, order ticket, position ticket, symbol, volume,
  price, profit, time (ms), direction (buy/sell), entry type (in/out/inout).
- **Subscription Request**: Represents a client's intent to receive events. Key attributes: starting
  time (optional, defaults to "now"), poll cadence (optional, server-bounded).
- **Subscription**: A long-lived server-side stream tied to one client, tracking the last delivered
  transaction so it can advance without gaps or duplicates.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After a trade transaction occurs on the account, a subscriber receives the corresponding
  event within one poll cadence interval under normal conditions.
- **SC-002**: A newly started subscription with default settings delivers zero historical transactions
  on an account that already has historical deals.
- **SC-003**: Across a test run of at least 100 transactions, each transaction is delivered exactly
  once per subscription, in chronological order, with zero duplicates and zero omissions.
- **SC-004**: Cancelling a subscription stops event delivery and releases server resources within a
  small, bounded time (no lingering background work after cancellation).
- **SC-005**: All existing operations continue to pass their current contract and behavior tests
  unchanged after the feature is added (zero regressions).
- **SC-006**: A client generated from the shared contract can subscribe to and consume events from the
  server with no manual code adjustment.
- **SC-007**: At least two concurrent subscriptions on the same account each receive the complete set
  of transactions for their respective starting points.

## Assumptions

- **Detection source is deals**: "Trade transaction" is defined as a newly added deal (emulating MQL5
  `OnTradeTransaction` `DEAL_ADD`). Tracking order-state or position-state changes as separate event
  types is out of scope for this version; deals capture executed trade actions, which is the primary
  user need.
- **Polling, not push**: Because the MetaTrader 5 Python API exposes no event callbacks, near-real-time
  delivery is achieved by server-side polling. "Real-time" is understood as "within one poll interval."
- **Default start is now**: An unspecified or zero starting time means the subscription begins at the
  current server time; historical backfill happens only when the client supplies an explicit past
  starting time.
- **Poll cadence bounds**: The server enforces a sensible default poll interval and a minimum floor to
  protect the terminal from excessive querying; a client-requested cadence below the floor is clamped.
- **Symbol scope**: The subscription covers all of the account's transactions; per-symbol filtering is
  not included in this version (can be added later without breaking the contract).
- **Resume is client-driven**: On disconnect, the consumer resumes by starting a new subscription with
  the last received transaction's timestamp; automatic in-library reconnection is not required for this
  version.
- **Single account per server**: Consistent with the existing services, the server operates against one
  connected MetaTrader 5 terminal/account; multi-account routing is out of scope.
- **Existing tooling reused**: Contract generation for both the server and the client library uses the
  project's existing generation workflow; no new build system is introduced.
