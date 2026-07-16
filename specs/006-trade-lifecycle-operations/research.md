# Phase 0 Research: Trade Lifecycle Operations

All technical context is resolved from the clarified feature specification, the
existing C# client/protobuf contracts, and the official MQL5 trade request and
return-code references. There are no open `NEEDS CLARIFICATION` items.

## Decision 1 - Keep the wire and server contracts unchanged

**Decision**: Implement the feature entirely in `mt5_grpc_client_csharp`. Reuse
`OrderSendService.SendOrder` for every trade submission and
`PositionsService.GetPositions` for batch discovery/refresh. Do not edit any
file under `protos/`, generated C# or Python bindings, or the Python server.

**Rationale**: `trade.proto` already contains all required action enum values,
`TradeRequest` fields (`position`, `position_by`, prices, time policy, magic,
comment), and the complete `TradeResult`. `position.proto` already supplies
symbol/ticket filtering and every field needed for eligibility and FIFO order.
The feature is therefore a backward-compatible client convenience surface.

**Alternatives considered**:
- Add five RPCs/messages: rejected because it duplicates an existing complete
  wire contract and increases server/multi-language compatibility work.
- Extend `PositionsGetRequest` with a repeated-ticket or magic filter: rejected
  because client-side intersection is sufficient and avoids an unnecessary
  contract/server release.

## Decision 2 - Public operation names and exact MT5 action mapping

**Decision**: Add five methods to `Mt5GrpcClient`:
`OpenOrderAsync`, `ClosePositionAsync`, `ModifyTradeAsync`,
`ClosePositionByAsync`, and `ClosePositionsByAsync` (plural batch). Map them as
follows:

| Intent | Action | Required identifier/type rule |
|--------|--------|-------------------------------|
| Open market buy/sell | `TradeActionDeal` | order type BUY or SELL |
| Place pending order | `TradeActionPending` | LIMIT/STOP/STOP_LIMIT type |
| Full/partial position close | `TradeActionDeal` | `position`; order side opposite the supplied position side |
| Modify position protection | `TradeActionSltp` | `position` |
| Modify pending order | `TradeActionModify` | `order` |
| Close by opposite position | `TradeActionCloseBy` | `position` plus unswapped `position_by` |

**Rationale**: This is the mapping already encoded by MT5's
`ENUM_TRADE_REQUEST_ACTIONS`. The official MQL5 request documentation also says
the position ticket identifies a modified/closed position and `position_by`
identifies the opposite position. The client selects the action; callers never
set it.

**Alternatives considered**:
- One method with a caller-supplied raw action: rejected because it recreates
  `SendOrderAsync` and does not deliver intent safety.
- Implicitly fetch position state for every close: rejected because a financial
  operation would gain a race-prone extra snapshot and violate the one-send/no-
  hidden-lookup requirement.

Sources: [MQL5 trade operation types](https://www.mql5.com/en/docs/constants/tradingconstants/enum_trade_request_actions),
[MQL5 trade request structure](https://www.mql5.com/en/docs/constants/structures/mqltraderequest).

## Decision 3 - Use operation-specific inputs and final-state modification data

**Decision**: Use dedicated C# input DTOs that omit `Action`. Required values are
constructor-supplied; optional trade values use nullable properties. A full
close has `Volume = null` and copies the supplied `CurrentVolume`; a partial
close sets a positive finite `Volume` no greater than that snapshot.

`ModifyTradeRequest` contains exactly one of a `PositionModification` or a
`PendingOrderModification`. Each variant describes the complete desired final
editable state. For position protection, zero explicitly clears SL/TP. For a
pending order, the caller supplies final price, stop-limit, SL, TP, time policy,
and applicable expiration. Supplying complete final state avoids treating
proto3 scalar defaults as "not present" and avoids a hidden account lookup.

At method entry the executor copies scalar values and clones reference-valued
protobuf data (for example `Timestamp`) into a new `TradeRequest`; it never
mutates a caller DTO, snapshot, or protobuf object.

**Rationale**: `TradeRequest.sl`, `tp`, `volume`, and enum fields are non-optional
proto3 scalars, so a patch-style C# DTO could not safely distinguish "unchanged"
from a wire default without also fetching or requiring current state. Explicit
final-state semantics are deterministic and preserve MT5's zero-as-clear
behavior.

**Alternatives considered**:
- Pass caller-created `TradeRequest`: rejected because it exposes the action and
  permits inconsistent action/type combinations.
- Patch semantics with unspecified scalar fields: rejected because the existing
  wire message cannot preserve that distinction and could accidentally clear or
  reset values.

## Decision 4 - Preserve call outcome and add a separate execution outcome

**Decision**: Single operations return `TradeOperationResult`, which contains
the unchanged `Mt5GrpcResult<OrderSendResponse>` as `CallResult` plus nullable
`TradeExecutionStatus`. `CallResult.IsSuccess` means a response with no shared
MT5 `Error` payload was received; it never means that the trade executed.
`ExecutionStatus` is null when no order-send response exists and otherwise is
classified from the raw `TradeResult.retcode` (or `Unknown` if the response
lacks a trade result). The original `OrderSendResponse` remains accessible, so
all raw result fields and the echoed request are preserved.

Initial conservative mapping:

| Raw code | Category |
|----------|----------|
| `10009 TRADE_RETCODE_DONE` | `Completed` |
| `10010 TRADE_RETCODE_DONE_PARTIAL` | `PartiallyCompleted` |
| `10008 TRADE_RETCODE_PLACED`, `10028 TRADE_RETCODE_LOCKED` | `AcceptedOrPlaced` |
| Every other currently documented trade-server code | `RejectedOrFailed` |
| Any unrecognized future code, or a response without `TradeResult` | `Unknown` |

The classifier accepts the lifecycle operation as an input so future
operation-specific mappings can be added without changing the public result.
No unrecognized, rejected, accepted, or transport-uncertain outcome is labeled
completed.

**Rationale**: MQL5 documents that the trade server places its result code in
`MqlTradeResult.retcode`; a successful transport call alone is not evidence of
execution. Keeping the raw response and adding a small derived category provides
both fidelity and safe ergonomics.

**Alternatives considered**:
- Convert every non-`DONE` retcode into a failed `Mt5GrpcResult`: rejected because
  it conflates a received trade response with transport/shared-error failure and
  discards accepted/partial distinctions.
- Treat unknown future codes as success: rejected as financially unsafe.

Source: [MQL5 trade server return codes](https://www.mql5.com/en/docs/constants/errorswarnings/enum_trade_return_codes).

## Decision 5 - Freeze membership, refresh by symbol, and pair deterministically

**Decision**: `ClosePositionsByAsync` performs this algorithm:

1. Validate a nonblank symbol before any RPC.
2. Call `GetPositions` once with that symbol; filter optional magic, positive
   ticket, BUY/SELL direction, positive finite volume, matching symbol, and a
   usable open timestamp; freeze the resulting ticket identities.
3. Before every pairing decision, call `GetPositions` once for the same symbol,
   intersect the response with the still-active frozen tickets, and discard any
   newly opened ticket. Positions that vanished or became structurally
   ineligible are recorded as remainders.
4. Sort current buys and sells independently by `(open_time, ticket)` ascending.
   Pair oldest buy with oldest sell. For deterministic request roles, the buy is
   `position` and the sell is `position_by`.
5. Submit exactly one close-by request. On `Completed` or
   `PartiallyCompleted`, leave both identities eligible for the next refresh so
   a larger surviving position can be paired again. On failed, accepted/placed,
   unknown, shared-error, or transport-uncertain outcomes, withhold both tickets
   for the rest of the invocation and continue with independent tickets.
6. Finish when no buy/sell pair remains; report unmatched active positions and
   all removed/withheld tickets.

The one refresh call may return new symbol positions, because the existing RPC
cannot filter by a ticket set; the intersection updates state only for the frozen
membership and never admits those new tickets.

**Rationale**: This exactly implements the clarified FIFO/ticket tie-breaker,
membership freeze, fresh-state decision, partial-volume reuse, and no-retry
requirements while using one refresh round trip per decision rather than one
round trip per ticket.

**Alternatives considered**:
- Re-query every frozen ticket separately: functionally valid but creates up to
  O(N^2) position RPCs and unnecessary latency.
- Re-run discovery after each pair: rejected because newly opened positions
  could enter the invocation.
- Precompute all pairs once: rejected because close-by can partially close one
  side and positions can change concurrently.

## Decision 6 - Batch cancellation, deadline, and partial-result semantics

**Decision**: Resolve one effective absolute deadline at batch entry (explicit
deadline, or the client's default relative deadline captured once) and pass it,
together with the same cancellation token, to discovery, refresh, and order-send
calls. Check cancellation/deadline before each new RPC. If either occurs during
an active RPC, retain that attempted pair and its call error, submit no later
pair, and materialize deterministic unattempted pairs/remainders from the last
known frozen state. A discovery/refresh failure stops the batch with a structured
batch error and no additional send. A rejected individual order response does
not stop independent later pairs.

**Rationale**: A batch is non-atomic and can have useful completed work before a
terminal condition. Returning a `MultipleCloseByResult` with batch status,
attempted pair results, unattempted pairs, and position remainders preserves that
work instead of replacing it with one outer exception.

**Alternatives considered**:
- Throw on the first failed pair: rejected because it loses prior results and
  violates continue-on-independent-failure behavior.
- Retry transport failures: rejected because the first trade may have executed
  even when the client did not receive its response.
- Give each inner call a newly extended default deadline: rejected because a
  caller cannot bound total batch duration.

## Decision 7 - Isolate pure mapping and use a scripted internal transport

**Decision**: Keep request validation/builders and retcode classification pure.
Place orchestration in an internal `TradeLifecycleExecutor` constructed with
send-position delegates (or a small internal transport interface). Production
delegates call the existing public client methods/invoker; unit tests use scripted
responses and call counters. Add `InternalsVisibleTo` for the unit-test assembly
if required.

**Rationale**: The current unit test fixture does not host an in-process gRPC
server. A narrow internal seam makes financially sensitive ordering, no-retry,
zero-submission validation, cancellation, and concurrent-state cases exhaustive
without a live terminal or broker.

**Alternatives considered**:
- Test only through a live MT5 server: rejected as non-reproducible and unsafe.
- Make `Mt5GrpcClient` virtual/inheritable for mocking: rejected because it
  expands the public compatibility surface unnecessarily.

## Decision 8 - Compatibility, versioning, logging, and documentation

**Decision**: Preserve `netstandard2.0;net472`, existing dependencies, proto
contract identity, tested server range, and `SendOrderAsync`. Release the additive
surface as the next client minor version (currently 4.2.0, so planned 4.3.0),
updating package release notes, `CHANGELOG.md`, README, examples, compatibility
tests, and package checks. Log lifecycle operation, batch item index, and status;
do not log credentials, default headers, or complete trade payloads.

**Rationale**: No wire/server compatibility changed. The existing package
already has the required dependencies and targets, so a client minor release is
sufficient and avoids forcing unrelated Python/server releases.

**Alternatives considered**:
- Major version: rejected because no existing public behavior is removed or
  changed.
- Coordinated server/proto version bump: rejected because their artifacts do not
  change.

## Resource bounds and reproducibility summary

- Single operations: one new request object, one existing unary order-send call,
  no retry or lookup.
- Batch: one initial symbol response, one active dictionary keyed by frozen
  ticket, one ordered pair/result list, and one remainder list; memory O(N).
  Network calls are one discovery, at most one refresh per pairing decision, and
  one send per attempted pair, all sequential.
- Tests: pure/scripted xUnit coverage requires no broker; existing contract,
  package, .NET Framework, generated-binding, and consumer checks remain the
  reproducible release gates.
