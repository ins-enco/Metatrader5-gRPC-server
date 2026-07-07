# Phase 1 Data Model: Trade Transaction Event Streaming

Entities are derived from the spec's *Key Entities* and *Requirements*. All wire
types live in the new `protos/trade_events.proto`; the shared `Error` type is
reused from `common.proto`. Field numbers shown are the frozen contract numbers.

---

## Entity: `TradeTransactionEvent`  *(stream item)*

One completed trade action observed on the account, derived from a newly added MT5
deal (FR-002, FR-003). Emitted at most once per subscription per deal.

| Field | Proto type | # | MT5 source | Notes / cross-language semantics |
|-------|-----------|---|------------|----------------------------------|
| `deal_ticket` | `uint64` | 1 | `deal.ticket` | Globally unique deal id; also the de-duplication key. 64-bit. |
| `order_ticket` | `uint64` | 2 | `deal.order` | Associated order ticket. 64-bit. |
| `position_ticket` | `uint64` | 3 | `deal.position_id` | Associated position ticket. 64-bit. |
| `symbol` | `string` | 4 | `deal.symbol` | Instrument name. |
| `volume` | `double` | 5 | `deal.volume` | Executed volume. Double precision (matches existing `Deal`). |
| `price` | `double` | 6 | `deal.price` | Execution price. Double. |
| `profit` | `double` | 7 | `deal.profit` | Realized profit for the deal. Double. |
| `time_msc` | `int64` | 8 | `deal.time_msc` | Transaction time, **milliseconds** since epoch. Ordering key. |
| `type` | `int32` | 9 | `deal.type` | Deal **direction** — verbatim MT5 `ENUM_DEAL_TYPE` numeric (0=BUY, 1=SELL, …). `int32` mirrors existing `Deal.type` for fidelity across languages. |
| `entry` | `int32` | 10 | `deal.entry` | Deal **entry** type — verbatim MT5 `ENUM_DEAL_ENTRY` numeric (0=IN, 1=OUT, 2=INOUT, …). Mirrors existing `Deal.entry`. |
| `error` | `optional Error` | 11 | `mt5.last_error()` | In-band error frame (FR-009). When set, the event is a **terminal error signal** (other fields empty) and the stream ends. Reuses `common.proto` `Error`. |

**Validation / invariants**
- A normal event has `error` **unset** and `deal_ticket > 0`.
- An error event has `error` **set**; consumers must check `error` before reading
  transaction fields.
- Direction/entry values are passed through **verbatim** from MT5 — the server does
  not remap them (Constitution II).

---

## Entity: `SubscribeTradeTransactionsRequest`  *(client → server, once at stream open)*

The client's intent to receive events (spec *Subscription Request*).

| Field | Proto type | # | Default / bounds | Notes |
|-------|-----------|---|------------------|-------|
| `from_time_msc` | `optional int64` | 1 | unset/`0` ⇒ **now**; older than `now − 7 days` ⇒ **clamped forward** to the 7-day cap | Start point, ms since epoch (FR-004, FR-005). |
| `poll_interval_ms` | `optional int64` | 2 | unset ⇒ **1000 ms**; `< 200` ⇒ **clamped up to 200 ms** | Poll cadence, ms (FR-007). |

**Validation rules (server-side, applied at stream open)**
1. Resolve start: `start = (from_time_msc unset or 0) ? now_ms : max(from_time_msc, now_ms − 7*24*3600*1000)`.
2. Resolve cadence: `cadence = (poll_interval_ms unset) ? 1000 : max(poll_interval_ms, 200)`.
3. If the MT5 terminal is not initialized/logged in ⇒ emit one error event
   (`mt5.last_error()`) and end (edge case: not initialized).

---

## Entity: `Subscription`  *(server-side runtime state, not on the wire)*

A long-lived server stream tied to one client, advancing without gaps/duplicates
(spec *Subscription*). Lives for the duration of one `SubscribeTradeTransactions`
call; occupies one server worker thread.

| State | Type | Purpose |
|-------|------|---------|
| `watermark` | `(time_msc: int64, ticket: uint64)` | Last delivered deal tuple; advances monotonically. De-dup + ordering (Decision 3). |
| `cadence_ms` | `int64` | Resolved poll interval (≥ 200). |
| `active` | derived from `context.is_active()` | Cancellation / disconnect signal → loop exits, worker released (FR-008, SC-004). |

**State transitions**
```
open ──resolve start/cadence──► polling
polling ──deals found──► emit ordered events, advance watermark ──► polling
polling ──no deals──► sleep(cadence) ──► polling            (stream stays healthy, FR: US1 #3)
polling ──client cancel / disconnect──► closed (worker freed)
polling ──terminal/persistent lookup failure──► emit Error event ──► closed
```

**Concurrency**: multiple `Subscription` instances run independently, each with its
own watermark; none shares mutable state (FR-010, SC-007).

---

## Relationships

```
SubscribeTradeTransactionsRequest ──opens──► Subscription (1 per stream)
Subscription ──polls history_deals_get──► [Deal…] ──maps 1:1──► TradeTransactionEvent (stream)
TradeTransactionEvent.error ──reuses──► common.proto Error
```

## Cross-Language Type Notes (Constitution III)

- **Time**: milliseconds as `int64` everywhere (`from_time_msc`, `time_msc`) —
  preserves ordering precision and avoids locale/second-truncation ambiguity. Note
  the MT5 `history_deals_get` filter is **second**-granular; the server queries from
  the watermark's floored second and filters precisely on the ms tuple.
- **Identifiers**: `uint64` for all tickets (deal/order/position), matching MT5 and
  the existing `Deal` message.
- **Money/volume/price**: `double`, matching existing messages.
- **Direction/entry**: `int32` carrying verbatim MT5 enum numerics (not a new proto
  `enum`), consistent with the existing `Deal.type`/`Deal.entry` encoding so no
  cross-language enum remap is required.
- **Optional fields**: proto3 `optional` with a documented zero/unset sentinel
  (`from_time_msc` unset/0 = "start now"), consistent with the rest of the contract.
