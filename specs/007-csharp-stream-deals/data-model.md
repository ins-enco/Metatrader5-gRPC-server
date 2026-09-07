# Phase 1 Data Model: C# Client StreamDeals Surface

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Date**: 2026-09-07

This feature introduces **no new wire type and no new field number**. Every entity below is
either an existing protobuf message (unchanged, listed for the rules that bind it) or a
client-side concept with no wire representation. The wire contract snapshot lives at
[contracts/deal.proto](./contracts/deal.proto); the authored public surface lives at
[contracts/client-surface.md](./contracts/client-surface.md).

---

## Wire entities (existing, unchanged)

### `DealsRequest` — the deal history read

| Field | № | Type | Role in this feature |
| --- | --- | --- | --- |
| `time_filter` | 1 | `TimeFilter` (oneof `filter`) | Time-window form: `date_from` / `date_to` |
| `ticket` | 2 | `uint64` (oneof `filter`) | Order-ticket form |
| `position` | 3 | `uint64` (oneof `filter`) | Position-ticket form |
| `group` | 4 | `optional string` | Symbol group filter; server substitutes `'*'` when unset |
| `chunk_size` | 5 | `optional uint32` | **`StreamDeals` only.** Deals per streamed message. Ignored by `GetDeals` |

**Validation rules** — all enforced by the server, none by the client (FR-005, FR-006):

- Exactly one `filter` form is active. No active form is **not** a client error: the server
  returns code `-2`, "No valid filter criteria provided", and the client surfaces it
  unchanged.
- `chunk_size` unset → server default 500. Set above 1000 → server clamps to 1000. Set to
  0 → the server's `_chunk_size_for` treats it as unset and applies 500. The client
  transmits whichever of these the caller produced and asserts nothing about it.
- The client MUST NOT construct a new request from the caller's fields; it forwards the
  caller's instance (`request ?? new DealsRequest()`), which is what preserves proto3
  presence for `group` and `chunk_size`.

**State transitions**: none. A request is immutable input to one read.

### `DealsResponse` — the deal chunk

| Field | № | Type | Role in this feature |
| --- | --- | --- | --- |
| `deals` | 1 | `repeated Deal` | One bounded slice of the filtered history (streaming), or the whole history (unary) |
| `error` | 2 | `optional Error` | Terminal failure; when present the message carries no deals |

**Validation rules**:

- A response whose `error.code != 0` is **terminal**. The chunk-level surface raises
  `Mt5GrpcClientException` carrying the mapped error and yields nothing further; the
  convenience surface converts it to a failure result. A response with `error` absent, or
  present with `code == 0`, is a normal data chunk — identical to the existing unary
  treatment in `Mt5GrpcErrorMapper.FromMt5Error`.
- Zero chunks is a valid, successful complete read (empty filtered history). It is never a
  failure and never an "empty with error" result.
- Chunk count and boundaries are server-chosen and carry no semantics: a caller must not
  infer anything from where a chunk ends.

**State transitions** (one stream, as observed by the client):

```text
open ──▶ data chunk ──▶ … ──▶ data chunk ──▶ completed        (normal read, 0..N chunks)
     └─▶ error-only chunk ─────────────────▶ faulted          (in-band MT5 / invalid request)
     └─▶ transport fault ──────────────────▶ faulted          (Unimplemented, Unavailable, …)
     └─▶ token cancelled / deadline ───────▶ cancelled        (call released)
     └─▶ caller breaks out of await foreach ▶ abandoned       (call released)
```

Every terminal state releases the underlying call (FR-008); `faulted` and `cancelled` carry
a mapped `Mt5GrpcError`.

### `Deal` — unchanged

All 18 fields (`ticket`, `order`, `time`, `time_msc`, `type`, `entry`, `magic`,
`position_id`, `reason`, `volume`, `price`, `commission`, `swap`, `profit`, `fee`,
`symbol`, `comment`, `external_id`) keep their numbers, types and MT5 meanings. The client
performs no per-deal mapping, filtering or reordering on either path — a streamed `Deal`
and a unary `Deal` for the same MT5 deal are byte-identical.

**Per-deal size**: ~77 bytes serialised, derived from the spec's production observations
(1571 deals ≈ 121 KB fatal, 982 deals ≈ 75 KB safe) rather than a controlled measurement.
This is what makes the 1000-deal cap (~77 KB) risky and the 500 default (~38 KB) safe. If a
controlled measurement disagrees, the guidance in the README and in
[contracts/client-surface.md](./contracts/client-surface.md) must be restated.

---

## Client-side entities (no wire representation)

### Deal chunk sequence — `IAsyncEnumerable<DealsResponse>`

The chunk-level surface's yielded type. One item per server message, in server order.
Retention is one chunk at a time: the enumerator holds the current `DealsResponse` and
nothing accumulates inside the library (SC-007). The caller decides what to keep.

### Concatenated read — the synthesized `DealsResponse`

The convenience surface's payload: a `DealsResponse` **constructed by the client**, not
received from the server, whose `Deals` is every chunk's `Deals` appended in stream order
and whose `Error` is always absent (a failure is reported through
`Mt5GrpcResult<T>.Error`, never inside the value).

Invariants, all test-enforced:

- `Value.Deals.Count` equals the sum of the chunk counts.
- Order equals concatenation order equals `GetDealsAsync` order, with the same tickets
  (FR-007, SC-002).
- Failure ⇒ `Value` is `null` and `Error` is the mapped error; no partial collection is
  ever returned (FR-004).
- Empty history ⇒ `IsSuccess` with `Deals.Count == 0` and `Error == null` (FR-003).
- Whole-history retention is the documented trade-off versus the chunk-level surface.

### Read anchor — caller-held, documentation only

The `time_msc` (or `time`) of the newest deal a consumer already holds, used as
`TimeFilter.date_from` on the next fetch so the unchanging closed history is not
re-transferred (FR-011, SC-008). The library stores nothing and offers no helper: the
anchor lives in consumer state. Documentation and both examples demonstrate backfill-once
through `StreamDealsAsync`, then incremental fetches from the anchor. Boundary handling is
the consumer's: an inclusive `date_from` may re-deliver the anchor deal, which a consumer
de-duplicates on `ticket`.

---

## Cross-Language Type Notes

- **Presence is meaningful for `chunk_size`.** Generated C# exposes `ChunkSize`,
  `HasChunkSize` and `ClearChunkSize()`. Unset must stay unset on the wire so
  `request.HasField('chunk_size')` is false server-side and the 500 default applies. A
  transmitted `0` is a distinct state that the server maps to its default; the client never
  converts between the two.
- **`group` follows the same rule.** Unset means the server's `'*'`; the client never
  materialises `'*'` itself.
- **The `filter` oneof** keeps exactly one active form; setting a second clears the first,
  per protobuf semantics, in every language.
- **Ordering** is the server's iteration order over the MT5 result. Within a chunk, order
  is repeated-field order; across chunks, gRPC preserves message order on a single stream.
  The caller-visible total order is the concatenation, and it equals the unary order for
  the same filters.
- **Time** stays as the contract defines it: `time` in Unix seconds (`int64`), `time_msc`
  in milliseconds since epoch (`int64`). No client-side `DateTime` conversion is
  introduced; the `deadline` parameter is a call deadline, unrelated to filter times.
- **Identifiers** stay `uint64` MT5 tickets, unconverted. A C# consumer sees `ulong`.
- **No sentinel values** are introduced by this feature, and none of the existing ones are
  reinterpreted.
- **`IAsyncEnumerable<T>` on `netstandard2.0`** comes from `Microsoft.Bcl.AsyncInterfaces`
   9.0.0, already referenced for `SubscribeTradeTransactionsAsync`. This is a C#-side
  language-surface concern only; a consumer in any other language reads the same stream
  through its own idiom.
