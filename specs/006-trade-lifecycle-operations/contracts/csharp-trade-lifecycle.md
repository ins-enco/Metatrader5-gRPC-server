# C# Client Contract: Trade Lifecycle Operations

**Status**: Phase 1 design contract  
**Scope**: `MetaTrader.Grpc.Client` public convenience surface  
**Wire impact**: None; existing protobuf and server contracts are reused unchanged

## Public operations

The following methods are added to the sealed partial `Mt5GrpcClient`. Parameter
ordering and optional deadline/cancellation behavior match existing unary
wrappers.

```csharp
Task<TradeOperationResult> OpenOrderAsync(
    OpenOrderRequest request,
    DateTime? deadline = null,
    CancellationToken cancellationToken = default);

Task<TradeOperationResult> ClosePositionAsync(
    ClosePositionRequest request,
    DateTime? deadline = null,
    CancellationToken cancellationToken = default);

Task<TradeOperationResult> ModifyTradeAsync(
    ModifyTradeRequest request,
    DateTime? deadline = null,
    CancellationToken cancellationToken = default);

Task<TradeOperationResult> ClosePositionByAsync(
    CloseByRequest request,
    DateTime? deadline = null,
    CancellationToken cancellationToken = default);

Task<MultipleCloseByResult> ClosePositionsByAsync(
    ClosePositionsByRequest request,
    DateTime? deadline = null,
    CancellationToken cancellationToken = default);
```

`SendOrderAsync(OrderSendRequest?, DateTime?, CancellationToken)` remains public
and source-compatible with its current implementation. It is still the escape
hatch for remove/cancel and future raw actions.

## Public input contract

Required data is constructor-supplied or otherwise validated before any RPC.
Inputs may expose normal C# properties for optional values, but the operation
must snapshot/copy them at entry and must not mutate them.

- `OpenOrderRequest`: symbol, generated order type, volume, optional price,
  stop-limit, SL/TP, deviation, filling/time policy, expiration, magic, comment.
- `ClosePositionRequest`: position ticket, symbol, `PositionSide`, current volume,
  optional partial volume, optional price, deviation, filling, magic, comment.
- `ModifyTradeRequest`: exactly one `PositionModification` or
  `PendingOrderModification` final-state target.
- `CloseByRequest`: primary position ticket, opposite ticket, optional magic and
  comment.
- `ClosePositionsByRequest`: required symbol, optional magic filter, optional
  comment for generated pair requests.

`PositionSide` has only `Buy` and `Sell`; it is separate from the generated order
enum so the close mapper must explicitly choose the opposite order type.

Position modification final-state contract:

```text
PositionTicket > 0
StopLoss       finite; 0 means clear
TakeProfit     finite; 0 means clear
```

Pending-order modification final-state contract:

```text
OrderTicket > 0
Price, StopLimitPrice, StopLoss, TakeProfit are finite final values
TimePolicy and applicable Expiration are supplied explicitly
0 protection value means clear; 0 stop-limit means not applicable
```

The caller copies the current value for any editable field it does not want to
change. This is intentional: the existing proto3 scalar fields cannot represent
a sparse "unchanged" patch, and the operation performs no hidden account lookup.

## Request mapping contract

Every call creates a new `TradeRequest` and one new `OrderSendRequest`. Caller
objects are not passed through or mutated.

| Operation/variant | `action` | Identifier fields | Other mapping |
|-------------------|----------|-------------------|---------------|
| Market open | DEAL | none | Symbol/type/volume and all applicable caller values. |
| Pending open | PENDING | none | Symbol/pending type/volume plus price/stop-limit/time/expiration/protection. |
| Position close | DEAL | `position` | Opposite BUY/SELL type; null requested volume uses current volume. |
| Position modify | SLTP | `position` only | Final SL and TP; no order id. |
| Pending modify | MODIFY | `order` only | Final price/stop-limit/SL/TP/time/expiration. |
| Close by | CLOSE_BY | `position`, `position_by` | Ticket roles preserved exactly; no swap. |

Single-operation call budget:

- zero RPCs on local validation failure;
- exactly one `SendOrder` RPC on valid input;
- zero implicit position/order lookups;
- zero implicit retries, regardless of transport or execution outcome.

MT5 remains authoritative for live position/order existence, available volume,
account mode, symbol/direction compatibility, permissions, price/fill rules,
market state, and broker limits.

## Validation contract

Local validation rejects at least:

- null request, blank required symbol, non-positive ticket, or identical close-by
  tickets;
- NaN/infinite numeric fields and non-positive volume;
- partial close volume greater than the caller's current-volume snapshot;
- CLOSE_BY order type in open, or a market/pending type outside its derived
  category;
- missing required stop-limit/expiration for a structurally applicable variant,
  or incompatible expiration/time-policy presence;
- modification with neither target or both targets.

Validation is returned as a failed `Mt5GrpcResult<OrderSendResponse>` inside
`TradeOperationResult`, with operation name and actionable message, null
execution status, and no RPC. Batch validation uses
`MultipleCloseByStatus.ValidationFailed` plus `BatchError` and no discovery/send.

## Single-result contract

```csharp
public sealed class TradeOperationResult
{
    public TradeLifecycleOperation Operation { get; }
    public Mt5GrpcResult<OrderSendResponse> CallResult { get; }
    public TradeExecutionStatus? ExecutionStatus { get; }
    public int? RawRetcode { get; }
}
```

Names may be implemented with equivalent read-only properties, but these
semantics are fixed:

- A rejected MT5 trade retcode can have `CallResult.IsSuccess == true` because a
  valid order-send response was received.
- `ExecutionStatus` is null only when no order-send response exists; a response
  missing `TradeResult` is `Unknown`.
- `CallResult.Value` exposes the original response and every raw `TradeResult`
  field, including the exact future/unknown retcode and echoed request.
- Transport cancellation/deadline and the shared `Error` payload retain existing
  `Mt5GrpcError` mapping.

Return-code categories are fixed for this version:

```text
DONE (10009)          -> Completed
DONE_PARTIAL (10010)  -> PartiallyCompleted
PLACED (10008)        -> AcceptedOrPlaced
LOCKED (10028)        -> AcceptedOrPlaced
other documented code -> RejectedOrFailed
unrecognized/missing  -> Unknown
```

## Multiple close-by contract

### Discovery and frozen membership

1. Validate symbol before any call.
2. Invoke `GetPositions` with the required symbol once.
3. Filter exact symbol, optional magic, positive ticket/volume, BUY/SELL type,
   and usable open time.
4. Freeze ticket identities. New tickets can never be admitted later.

If discovery returns no eligible opposite positions, return Completed with no
pairs (and any unmatched eligible positions as remainders) and no `SendOrder`.

### Refresh and pairing

Before each decision, issue one symbol-scoped `GetPositions`, then intersect with
active frozen identities. Ignore new tickets. Record missing or newly ineligible
frozen positions. Sort BUY and SELL independently by raw protobuf timestamp then
ticket ascending. Pair oldest BUY as `position` and oldest SELL as `position_by`.

Submit pairs sequentially. There is no parallel order submission and no rollback.

### Post-attempt rules

- `Completed` or `PartiallyCompleted`: do not assume which ticket remains; refresh
  both on the next decision. A surviving larger position can be paired again.
- `RejectedOrFailed`, `AcceptedOrPlaced`, `Unknown`, shared-error failure, or
  transport uncertainty: record once and withhold both tickets for the rest of
  the invocation. Continue with independent tickets unless cancellation/deadline
  stopped the batch.
- Cancellation/deadline: retain the active attempted result if any, submit no new
  pair, and classify the deterministic remaining frozen work as unattempted or
  remainder.
- Discovery/refresh failure: retain prior results, stop without a new send, and
  expose a batch error.

The batch uses one effective absolute deadline captured at entry. The same
cancellation token and deadline flow through discovery, refresh, and sends.

### Batch result contract

`MultipleCloseByResult` contains:

- status and optional batch error;
- immutable initial frozen-ticket order;
- ordered `CloseByPairOutcome` values with one-based index and both preserved
  ticket roles;
- per-attempt `TradeOperationResult`, preserving call and execution outcomes;
- materialized unattempted pairs after cancellation/deadline where determinable;
- ordered position remainders with last-known volume and reason.

One rejected pair never discards prior outcomes or prevents a later independent
pair. Failed/uncertain tickets are never retried automatically.

## Observability and security

Use existing structured logging infrastructure. Log operation name and, for
batch calls, item index and terminal category. Do not log authentication headers,
account credentials, or complete trade requests/responses. Avoid logging comment
text and full symbol/account payloads by default.

## Compatibility guarantees

- No `.proto`, RPC, message, field number, generated binding, server behavior, or
  Python package change.
- Existing `SendOrderAsync` behavior and all existing public members remain
  unchanged.
- New types compile for both package targets (`netstandard2.0`, `net472`) and are
  consumable from a `net48` example/application.
- Existing proto contract identity and tested server range stay unchanged.
- Package release is additive minor (planned 4.3.0 from current 4.2.0).
