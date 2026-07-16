# Phase 1 Data Model: Trade Lifecycle Operations

This feature adds C# client-domain types only. Existing protobuf messages remain
the wire model and are not changed. Every public input is copied into a fresh
`TradeRequest`; every returned `OrderSendResponse` remains intact.

## Input entities

### `OpenOrderRequest`

Intent to open a market position or place a pending order without exposing the
raw MT5 action.

| Field | C# type | Required | Rules / mapping |
|-------|---------|----------|-----------------|
| `Symbol` | `string` | yes | Nonblank; copied to `TradeRequest.Symbol`. |
| `Type` | `ENUM_ORDER_TYPE` | yes | BUY/SELL selects DEAL; LIMIT/STOP/STOP_LIMIT selects PENDING; CLOSE_BY rejected. |
| `Volume` | `double` | yes | Positive and finite. |
| `Price` | `double?` | no | If present, finite; preserved with proto presence. |
| `StopLimitPrice` | `double?` | no | If present, finite; required where structurally applicable to stop-limit types. |
| `StopLoss` / `TakeProfit` | `double?` | no | If present, finite; zero is preserved. |
| `Deviation` | `int` | no | Preserved verbatim; broker rules remain authoritative. |
| `FillingPolicy` | `ENUM_ORDER_TYPE_FILLING` | yes/defaulted | Preserved. |
| `TimePolicy` | `ENUM_ORDER_TYPE_TIME` | yes/defaulted | Preserved; specified policies require expiration. |
| `Expiration` | `Timestamp?` | conditional | Cloned; present only for a compatible time policy. |
| `Magic` | `int` | no | Preserved. |
| `Comment` | `string?` | no | Preserved with presence. |

Validation failure creates a structured failed call result and performs zero
order submissions.

### `ClosePositionRequest`

Caller-supplied current position facts plus execution options. No account lookup
is performed.

| Field | C# type | Required | Rules / mapping |
|-------|---------|----------|-----------------|
| `PositionTicket` | `long` | yes | Greater than zero; maps to `position`. |
| `Symbol` | `string` | yes | Nonblank; maps to `symbol`. |
| `Side` | `PositionSide` | yes | BUY maps to SELL request type; SELL maps to BUY. |
| `CurrentVolume` | `double` | yes | Positive and finite; is the full-close volume. |
| `Volume` | `double?` | no | Null = full close; otherwise positive, finite, and no greater than `CurrentVolume`. |
| `Price` | `double?` | no | Optional for execution modes where MT5 does not require price; finite if present. |
| `Deviation`, `FillingPolicy`, `Magic`, `Comment` | existing scalar/enum/string types | no | Copied to the new DEAL request. |

### `ModifyTradeRequest`

Union-like request with exactly one target member.

| Field | Type | Cardinality |
|-------|------|-------------|
| `Position` | `PositionModification?` | Exactly one of `Position` / `PendingOrder`. |
| `PendingOrder` | `PendingOrderModification?` | Exactly one of `Position` / `PendingOrder`. |

Both set or neither set is a local validation failure with zero order submission.

#### `PositionModification`

| Field | Type | Rules |
|-------|------|-------|
| `PositionTicket` | `long` | Positive; maps to `position`; action SLTP. |
| `StopLoss` | `double` | Desired final value; finite; zero explicitly clears. |
| `TakeProfit` | `double` | Desired final value; finite; zero explicitly clears. |

The caller supplies both desired final protection values. To change only one,
copy the current value for the other; this avoids an implicit lookup and avoids
confusing a proto3 zero with "unchanged".

#### `PendingOrderModification`

| Field | Type | Rules |
|-------|------|-------|
| `OrderTicket` | `long` | Positive; maps to `order`; action MODIFY. |
| `Price` | `double` | Desired final price; finite. |
| `StopLimitPrice` | `double` | Desired final stop-limit value; finite; zero where not applicable. |
| `StopLoss` / `TakeProfit` | `double` | Desired final values; finite; zero clears. |
| `TimePolicy` | `ENUM_ORDER_TYPE_TIME` | Desired final policy. |
| `Expiration` | `Timestamp?` | Cloned; required/forbidden according to the selected policy. |

These are final-state values rather than a sparse patch because the existing
wire scalars do not carry presence for every editable field.

### `CloseByRequest`

| Field | Type | Rules |
|-------|------|-------|
| `PositionTicket` | `long` | Positive; maps to `position`. |
| `OppositePositionTicket` | `long` | Positive and distinct; maps to `position_by` without swapping. |
| `Magic` | `int` | Optional/defaulted; preserved. |
| `Comment` | `string?` | Optional; preserved. |

Current existence, symbol/direction compatibility, and hedging-account support
are deliberately left to MT5.

### `ClosePositionsByRequest`

| Field | Type | Rules |
|-------|------|-------|
| `Symbol` | `string` | Required and nonblank before discovery. |
| `Magic` | `int?` | Optional equality filter applied after symbol discovery. |
| `Comment` | `string?` | Applied to each generated close-by request. |

The input is snapshotted at method entry and never mutated.

## Result entities

### `TradeOperationResult`

Result of one dedicated order submission.

| Field | Type | Meaning |
|-------|------|---------|
| `Operation` | `TradeLifecycleOperation` | Open, Close, ModifyPosition, ModifyPendingOrder, or CloseBy. |
| `CallResult` | `Mt5GrpcResult<OrderSendResponse>` | Existing success/shared-error/transport/cancellation/deadline information. |
| `ExecutionStatus` | `TradeExecutionStatus?` | Null when no response; otherwise derived from raw retcode, with missing result = Unknown. |
| `RawRetcode` | `int?` convenience | Exact `TradeResult.Retcode` when available; never replaces the response. |

Invariants:
- `CallResult.IsSuccess` means a usable order-send response was received, not
  that execution completed.
- `CallResult.Value` is preserved unchanged, including deal/order tickets,
  volume, price/bid/ask, comment, request id, external code, and echoed request.
- Validation failure has `CallResult.Error`, null execution status, and zero RPCs.

### `TradeExecutionStatus`

| Value | Meaning |
|-------|---------|
| `Completed` | MT5 returned DONE. |
| `PartiallyCompleted` | MT5 returned DONE_PARTIAL. |
| `AcceptedOrPlaced` | MT5 returned PLACED or LOCKED; completion is not asserted. |
| `RejectedOrFailed` | A documented non-success/non-accepted trade code. |
| `Unknown` | Future unrecognized code or a response without a trade result. |

### `MultipleCloseByResult`

Durable partial result for the whole non-atomic batch.

| Field | Type | Meaning |
|-------|------|---------|
| `Status` | `MultipleCloseByStatus` | Completed, ValidationFailed, DiscoveryFailed, RefreshFailed, Cancelled, or DeadlineExceeded. |
| `BatchError` | `Mt5GrpcError?` | Validation/discovery/refresh/terminal error; pair errors remain on the pair. |
| `FrozenTickets` | `IReadOnlyList<long>` | Initial eligible membership in deterministic order. |
| `Pairs` | `IReadOnlyList<CloseByPairOutcome>` | Ordered attempted and materialized unattempted pairs. |
| `Remainders` | `IReadOnlyList<PositionRemainder>` | Frozen positions not represented as an attempted/completed pair remainder. |

Collections are copied/read-only and preserve deterministic order.

### `CloseByPairOutcome`

| Field | Type | Meaning |
|-------|------|---------|
| `PairIndex` | `int` | One-based pairing decision order. |
| `PositionTicket` | `long` | Selected BUY ticket; primary `position` role. |
| `OppositePositionTicket` | `long` | Selected SELL ticket; `position_by` role. |
| `AttemptState` | `PairAttemptState` | Attempted or Unattempted. |
| `OperationResult` | `TradeOperationResult?` | Present only for attempted pairs. |

A received rejected result or a call with uncertain/no response is recorded once;
its two tickets are withheld and never retried in the invocation.

### `PositionRemainder`

| Field | Type | Meaning |
|-------|------|---------|
| `Ticket` | `long` | Frozen ticket identity. |
| `LastKnownVolume` | `double?` | Latest refreshed remaining volume when known. |
| `Reason` | `PositionRemainderReason` | NoOpposite, BecameIneligible, WithheldAfterPair, UnattemptedAfterStop, MissingFromRefresh, or InvalidSnapshot. |

## Batch runtime state and transitions

Invocation-local state:

| State | Type | Purpose |
|-------|------|---------|
| `frozenTickets` | ordered set of `long` | Immutable membership from initial discovery. |
| `active` | dictionary ticket -> cloned current `Position` | Only frozen, still-eligible positions. |
| `withheld` | set of `long` | Failed/accepted/unknown/uncertain pair tickets; never paired again. |
| `pairs` | ordered list | Audit record of each decision/attempt. |
| `remainders` | ordered list | Removed, unmatched, or stopped positions. |
| `effectiveDeadline` | `DateTime?` | One absolute batch deadline captured at entry. |

State flow:

```text
validate
  -> discover symbol positions
  -> filter magic/eligibility and freeze ticket identities
  -> refresh symbol positions and intersect frozen active set
  -> sort BUY and SELL by (open time, ticket)
  -> no opposite pair: complete and report remainder
  -> pair oldest BUY as position with oldest SELL as position_by
  -> send once
     -> completed/partial: refresh both on next decision; surviving volume may pair again
     -> rejected/accepted/unknown/call failure: withhold both; continue independently
     -> cancellation/deadline: retain attempt, materialize unattempted remainder, stop
```

New positions returned by refresh are ignored because their tickets are absent
from `frozenTickets`. A refresh failure performs no order submission for that
decision and stops the batch with retained prior outcomes.

## Relationships to existing protobuf entities

```text
operation DTO -> new TradeRequest -> existing OrderSendRequest -> SendOrder RPC
                                                        -> TradeOperationResult

ClosePositionsByRequest -> PositionsGetRequest(symbol) -> frozen Position set
frozen Position set + refreshed Position set -> CloseByPairOutcome[]
```

No new wire entity, persistence entity, or generated binding is introduced.
