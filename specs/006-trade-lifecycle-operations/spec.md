# Feature Specification: Trade Lifecycle Operations

**Feature Branch**: `007-trade-lifecycle-operations`  
**Created**: 2026-07-16  
**Status**: Draft  
**Input**: User description: "C# client library for the MetaTrader 5 gRPC server. Expand the existing trading API (`SendOrderAsync`) to support dedicated operations for opening, closing, modifying, closing by, and multiple close-by orders. This improvement provides a clearer and more complete way to manage the full lifecycle of trades, making the API easier to use, maintain, and extend for future trading features."

## Clarifications

### Session 2026-07-16

- Q: How should multiple close-by select its positions? → A: Automatically discover and pair all eligible opposite positions.
- Q: What positions may automatic multiple close-by consider? → A: Require a symbol and optionally filter by magic number.
- Q: How should automatic discovery pair eligible opposite positions? → A: Pair oldest buy with oldest sell, breaking ties by ascending ticket.
- Q: How should multiple close-by handle position changes during processing? → A: Freeze initially discovered tickets, refresh their state before each pair, and exclude newly opened positions.
- Q: How should dedicated operations classify an unsuccessful MT5 trade return code? → A: Keep call/transport status separate and expose an explicit execution status derived from the MT5 return code.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Open and Close a Position (Priority: P1)

A client-library user opens a market position or places a pending order through
an operation whose intent is explicit, and later closes all or part of an open
position through a dedicated close operation. The user supplies trade details
but does not manually select the low-level MT5 trade action for either task.

**Why this priority**: Opening and closing are the minimum useful trade
lifecycle. Making these high-risk actions explicit reduces action-selection
mistakes while retaining the existing trade result and error information.

**Independent Test**: Submit one market opening, one pending opening, one full
position close, and one partial position close through the dedicated operations;
verify that each produces the appropriate MT5 request and exposes separate call
and trade-execution outcomes.

**Acceptance Scenarios**:

1. **Given** valid market-order details, **When** a user invokes the dedicated
   open operation, **Then** one immediate-execution request is submitted and the
   complete trade result is returned.
2. **Given** valid pending-order details, **When** a user invokes the dedicated
   open operation, **Then** one pending-order request is submitted with its
   price, time policy, expiration, and protection values preserved.
3. **Given** a current position and its full remaining volume, **When** a user
   invokes the dedicated close operation, **Then** one opposite-side deal
   request identifies that position and requests its full closure.
4. **Given** a current position and a positive volume smaller than its remaining
   volume, **When** a user invokes the dedicated close operation, **Then** one
   opposite-side deal request identifies that position and requests only that
   volume.
5. **Given** missing or structurally invalid opening or closing inputs, **When**
   the operation is invoked, **Then** it fails clearly before any trade request
   is submitted.
6. **Given** MT5 returns an order-send response with a non-success trade return
   code, **When** the user inspects the dedicated operation result, **Then** the
   call outcome shows that a response was received while the execution outcome
   shows that the requested trade did not complete successfully.

---

### User Story 2 - Modify an Existing Trade (Priority: P2)

A client-library user modifies the protection values of an open position or the
editable parameters of a pending order through a dedicated modification
operation. The target kind determines the MT5 action, so the user does not need
to remember whether position protection and pending-order changes use different
actions.

**Why this priority**: Trade management after entry is essential for risk
control. MT5 distinguishes position protection changes from pending-order
changes, and a dedicated operation can make that distinction explicit and safe.

**Independent Test**: Modify the stop-loss/take-profit values of one open
position and modify the price and expiration of one pending order; verify that
each request identifies the correct target and retains all provided values.

**Acceptance Scenarios**:

1. **Given** an open position ticket and new stop-loss and/or take-profit
   values, **When** a user invokes the modification operation for a position,
   **Then** one position-protection change request is submitted for that ticket.
2. **Given** a pending-order ticket and valid editable values, **When** a user
   invokes the modification operation for a pending order, **Then** one pending-
   order modification request is submitted for that ticket.
3. **Given** a modification request that identifies neither a position nor a
   pending order, or identifies both, **When** it is invoked, **Then** it fails
   clearly before any trade request is submitted.

---

### User Story 3 - Close One Position by an Opposite Position (Priority: P3)

A user on a hedging account closes a position by an opposite position for the
same symbol through a dedicated close-by operation. The operation takes the two
position tickets directly and preserves the MT5 server's execution result.

**Why this priority**: Close-by is financially distinct from sending an
opposite market deal and can reduce spread costs, but its required pair of
position identifiers is easy to construct incorrectly through the generic
trade request.

**Independent Test**: Supply two distinct, opposite position tickets for the
same symbol on a hedging account and verify that exactly one close-by request is
submitted with both ticket roles preserved.

**Acceptance Scenarios**:

1. **Given** two distinct opposite positions for the same symbol on a hedging
   account, **When** the user invokes close-by, **Then** one close-by request is
   submitted with the first ticket as the position and the second as the
   opposite position.
2. **Given** the same ticket in both roles or a non-positive ticket, **When** the
   user invokes close-by, **Then** it fails clearly before any trade request is
   submitted.
3. **Given** tickets that no longer exist, have the same direction, use
   different symbols, or belong to an account that does not support close-by,
   **When** the close-by request reaches MT5, **Then** the MT5 rejection and
   return code remain available to the user without being presented as a
   successful execution.

---

### User Story 4 - Process Multiple Close-By Pairs (Priority: P4)

A user invokes multiple close-by for one required symbol and, when provided, one
magic number. The client discovers currently eligible opposite positions within
that scope, freezes those ticket identities for the invocation, refreshes their
state before each pairing decision, and returns an ordered outcome for every pair
that was attempted. Newly opened positions are excluded. One rejected pair does
not hide prior outcomes or prevent independent later pairs from being attempted.

**Why this priority**: Hedging strategies often need to offset several
positions. A batch convenience operation removes repetitive caller code while
keeping each MT5 trade operation observable and independently auditable.

**Independent Test**: Provide a position set from which three eligible pairs can
be discovered, cause the second pair to be rejected, and verify that all three
pairing decisions and outcomes are returned in deterministic order.

**Acceptance Scenarios**:

1. **Given** a required symbol and an optional magic-number filter whose matching
   positions include eligible opposites, **When** the user invokes the multiple
   close-by operation, **Then** the client discovers and freezes only matching
   tickets, refreshes their current state, pairs the oldest eligible buy with the
   oldest eligible sell using ascending ticket as the equal-time tie-breaker,
   submits pairs sequentially, and returns correspondingly ordered outcomes.
2. **Given** the second automatically discovered pair is rejected by MT5,
   **When** processing completes, **Then** the first result, the second failure,
   and the later pair results are all retained and associated with the pairs the
   client selected.
3. **Given** no eligible opposite positions are discovered, **When** the user
   invokes the multiple close-by operation, **Then** it completes with an empty
   summary and submits no trade request.
4. **Given** cancellation or deadline expiry during a batch, **When** the active
   request completes or is cancelled, **Then** no new pair is submitted and the
   caller can distinguish completed, failed, and unattempted pairs.
5. **Given** a new matching position opens after initial discovery, **When** the
   batch refreshes position state, **Then** that new ticket is not added to the
   invocation and produces no close-by request or outcome.

### Edge Cases

- A market or pending order uses a type that is inconsistent with the selected
  opening variant.
- A close request specifies zero, negative, non-finite, or greater-than-current
  volume; structurally invalid volume is rejected locally, while account-state
  and broker-limit validation remains authoritative at MT5.
- A modification supplies no changed values, clears only one protection value,
  or uses an expiration that is inconsistent with the selected time policy.
- A position or order changes after the caller captured its details but before
  MT5 processes the operation; the returned MT5 result remains authoritative.
- A close-by pair is structurally valid but the positions are no longer open,
  are not opposite, use different symbols, or are used on a netting account.
- Automatic discovery is requested with a missing or blank symbol; discovery
  fails before retrieving positions or submitting a trade request.
- A magic-number filter is supplied and matching positions coexist with other
  positions on the same symbol; only the matching positions are eligible.
- The discovered position set contains unequal buy and sell volumes or counts;
  FIFO pairing continues with the oldest remaining opposite positions and
  reports any eligible remainder that cannot be paired.
- A position becomes ineligible after discovery but before its close-by request
  is processed; it is removed from remaining eligibility and reported, while
  later pairing decisions use refreshed state for the other frozen tickets.
- A close-by response is failed or has an uncertain transport outcome while both
  tickets still appear open; that pair is not retried automatically, and its
  tickets are withheld from further automatic pairing in the invocation.
- MT5 accepts a request for processing but returns a trade return code that does
  not represent completed execution. The result is not relabeled as execution
  success.
- A transport failure, deadline, or cancellation occurs before MT5 returns a
  result, leaving execution state uncertain. The outcome preserves that
  uncertainty and does not retry automatically.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The client library MUST expose dedicated asynchronous operations
  for opening, closing, modifying, closing by, and processing multiple close-by
  position pairs.
- **FR-002**: A user of a dedicated operation MUST NOT need to set the underlying
  MT5 trade-action value manually; each operation MUST choose the action that
  corresponds to the requested lifecycle intent.
- **FR-003**: The open operation MUST support both immediate market orders and
  pending orders and MUST map them to immediate-execution and pending-order
  actions respectively.
- **FR-004**: The open operation MUST preserve all applicable caller-provided
  values, including symbol, side/type, volume, price, stop-limit price,
  stop-loss, take-profit, deviation, filling policy, time policy, expiration,
  magic identifier, and comment.
- **FR-005**: The close operation MUST identify the position being closed and
  submit an opposite-side immediate deal using the caller-provided symbol,
  current side, volume, execution price when applicable, deviation, filling
  policy, magic identifier, and comment.
- **FR-006**: The close operation MUST support both full and partial closure. A
  partial close MUST require an explicit positive finite volume; a full close
  MUST use the caller-provided current position volume rather than assuming an
  unverified value.
- **FR-007**: The modification operation MUST distinguish an open-position
  protection change from a pending-order parameter change and MUST select the
  position-protection or pending-order-modification action accordingly.
- **FR-008**: A position modification MUST identify exactly one position and
  support changing stop-loss and/or take-profit values, including explicitly
  clearing either value when MT5 permits it.
- **FR-009**: A pending-order modification MUST identify exactly one order and
  support all editable fields already represented by the trade contract,
  including price, stop-limit price, stop-loss, take-profit, time policy, and
  expiration where applicable.
- **FR-010**: A single close-by operation MUST require two positive, distinct
  position tickets and MUST map them to the primary-position and opposite-
  position roles without swapping them.
- **FR-011**: Client-side validation MUST reject inputs that are invalid without
  current account state, including missing required values, non-positive
  identifiers, identical close-by tickets, unsupported open-order type/action
  combinations, non-finite numeric values, and ambiguous modification targets.
- **FR-012**: Account-state and broker-dependent validation—including position
  existence, available volume, symbol and direction compatibility, account
  margin mode, trading permissions, market state, price rules, and broker
  limits—MUST remain authoritative at MT5, and its result MUST be preserved.
- **FR-013**: Each dedicated single-order operation MUST submit no more than one
  underlying order-send request and MUST NOT perform an implicit retry, because
  retrying an execution request can duplicate a trade when the first outcome is
  uncertain.
- **FR-014**: All single-order operations MUST return the same typed success,
  transport failure, cancellation/deadline failure, and MT5 error-payload
  information available through the existing generic send operation. When a
  trade response is received, the dedicated result MUST separately expose its
  trade-execution status.
- **FR-015**: The trade-execution status MUST be derived from the MT5 trade return
  code using operation-aware categories that distinguish completed execution,
  partial execution, accepted or placed requests, rejected or failed requests,
  and unknown outcomes. A successful outer call MUST NOT by itself be represented
  as proof that the trade executed successfully.
- **FR-016**: The multiple close-by operation MUST discover all currently
  eligible opposite positions for one required symbol and, when supplied, one
  magic-number filter. It MUST sort buy and sell positions independently by
  ascending open time and then ascending ticket, pair the oldest buy with the
  oldest sell, and process pairs sequentially using the same behavior as the
  single close-by operation.
- **FR-017**: The multiple close-by operation MUST return one ordered outcome for
  every attempted pair, and every outcome MUST retain both automatically selected
  ticket roles, the pairing order, the call/transport status, the explicit trade-
  execution status when a response exists, and the full result or failure for
  that pair.
- **FR-018**: Failure of an individual close-by pair MUST NOT discard earlier
  outcomes or automatically prevent later independent pairs from being
  attempted. Cancellation or deadline expiry MUST stop submission of new pairs
  and identify the unattempted discovered remainder.
- **FR-019**: A missing or blank discovery symbol MUST fail before retrieving
  positions or submitting an order. If discovery finds no eligible pair for the
  required symbol and optional magic-number filter, the operation MUST return an
  empty completed result without an order-send request.
- **FR-020**: The batch operation MUST be documented as non-atomic: it MUST NOT
  promise rollback, and a later failure MUST NOT reverse an earlier successful
  close-by operation. Its result MUST also identify eligible discovered
  positions that remained unpaired or unattempted.
- **FR-021**: Dedicated operations MUST honor the client's existing deadline,
  cancellation, security, logging, and error-mapping behavior. Logs MUST identify
  the lifecycle operation and batch item when applicable without exposing
  account credentials or dumping complete trade payloads.
- **FR-022**: The existing generic `SendOrderAsync` operation MUST remain
  available with unchanged source behavior and remain the escape hatch for
  advanced or future MT5 trade requests not covered by dedicated operations.
- **FR-023**: Dedicated operations MUST NOT mutate request objects, position
  snapshots, order snapshots, or discovery criteria supplied by the caller.
- **FR-024**: Documentation MUST include independently runnable examples for all
  five operation categories, explain full versus partial close, distinguish
  position modification from pending-order modification, state close-by's
  hedging-account constraints, show per-pair batch result inspection, and warn
  against automatic retry after uncertain execution outcomes.
- **FR-025**: The dedicated operations and their result types MUST remain usable
  on every target framework supported by the current client package, including
  the existing .NET Framework compatibility target.
- **FR-026**: Multiple close-by MUST freeze the ticket identities found by its
  initial discovery. Before each pairing decision it MUST refresh current state
  for only those tickets, remove tickets that are no longer eligible, and MUST
  NOT add positions opened after initial discovery.
- **FR-027**: A failed close-by pair or a pair with an uncertain transport
  outcome MUST NOT be retried automatically. Its tickets MUST be withheld from
  further automatic pairing during that invocation and identified in the final
  outcome; successfully partially closed positions MAY remain eligible at their
  refreshed remaining volume.
- **FR-028**: Every dedicated result that contains a trade response MUST retain
  the raw MT5 return code, deal ticket, order ticket, executed volume, prices,
  comment, request identifier, external return code, and echoed request. An
  unrecognized future return code MUST be preserved exactly, classified as an
  unknown execution outcome, and MUST NOT be assumed successful.

### Protocol and MT5 Contract Impact *(mandatory)*

- **Affected RPCs/Messages**: Existing `OrderSendService.SendOrder`,
  `OrderSendRequest.trade_request`, `OrderSendResponse`, `TradeRequest`, and
  `TradeResult` are reused. No new RPC, message, field, enum value, or field
  number is required. The existing client `SendOrderAsync` remains unchanged;
  the new behavior is an additive convenience surface over it.
- **Compatibility Decision**: Backward-compatible client-library addition. Wire
  behavior and server behavior do not change. The client package can receive an
  additive minor-version release; existing callers can continue using the
  generic send operation without migration.
- **MT5 Operation Mapping**: Market open and position close use
  `TRADE_ACTION_DEAL`; pending open uses `TRADE_ACTION_PENDING`; position
  stop-loss/take-profit modification uses `TRADE_ACTION_SLTP`; pending-order
  modification uses `TRADE_ACTION_MODIFY`; close-by uses
  `TRADE_ACTION_CLOSE_BY` with `position` and `position_by`. Multiple close-by
  invokes the same close-by mapping once per ordered pair. MT5 return codes and
  terminal error payloads remain authoritative and are never replaced by a
  client-defined execution-success interpretation.
- **Cross-Language Type Notes**: No shared contract type changes. Existing
  optional-field presence, 64-bit ticket and request identifiers, double-valued
  volume and prices, timestamp expiration, enums, and sentinel/default behavior
  remain unchanged. The convenience operations are scoped to the C# package;
  other generated clients continue to use the same language-neutral RPC.

### Key Entities *(include if feature involves data)*

- **Lifecycle Operation Input**: Operation-specific trade details from which the
  client forms one valid MT5 trade request without requiring a raw action value.
- **Multiple Close-By Scope**: One required symbol and one optional magic number
  defining the only account positions eligible for initial automatic discovery;
  the discovered ticket identities form a fixed membership set for the invocation.
- **Position Reference**: A positive position ticket plus the current symbol,
  side, and volume needed for closing or modifying that position.
- **Pending Order Reference**: A positive pending-order ticket plus the editable
  price, protection, and expiration values relevant to modification.
- **Close-By Pair**: A deterministically selected primary position ticket and
  opposite position ticket discovered from the current eligible position set;
  the oldest buy and oldest sell are selected first, equal open times are ordered
  by ascending ticket, and the selected ticket roles and order are preserved.
- **Single Operation Outcome**: The existing typed result containing either the
  order-send response and full MT5 trade result or a structured client,
  transport, cancellation/deadline, or server error. A received trade response
  carries a separate operation-aware execution status derived from its raw MT5
  return code.
- **Multiple Close-By Outcome**: An ordered summary of discovered pair outcomes
  plus any eligible positions left unpaired and any pairs left unattempted after
  cancellation or deadline expiry; every attempted pair retains separate call
  and trade-execution status.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Developers can express all five requested operation categories
  through one clearly named operation each, with zero manually assigned MT5
  trade-action values in caller code.
- **SC-002**: Contract tests verify 100% correct action and identifier mapping
  for market open, pending open, full close, partial close, position modify,
  pending-order modify, and close-by scenarios, with zero mapping mismatches.
- **SC-003**: Every structurally invalid input case defined in FR-011 and FR-019
  produces zero order submissions and a specific actionable failure.
- **SC-004**: Each dedicated single-order operation uses exactly one order-send
  submission and adds no implicit account lookup or retry.
- **SC-005**: For `N` eligible close-by pairs automatically discovered within the
  required symbol and optional magic-number scope when the operation is not
  cancelled, the result contains exactly `N` outcomes in the documented
  oldest-first pairing order with ascending-ticket tie-breaking, including when
  any subset is rejected; positions outside the scope produce zero outcomes.
- **SC-006**: In cancellation and deadline tests, zero new pairs are submitted
  after cancellation/deadline detection, and 100% of discovered pairs are
  classified as completed, failed, or unattempted.
- **SC-007**: Existing generic send-operation contract tests continue to pass
  unchanged, demonstrating no regression for current callers.
- **SC-008**: The five documented examples compile on all supported client
  targets and let a developer inspect the MT5 trade return code or structured
  failure for every submitted operation.
- **SC-009**: In a documentation usability review, a developer familiar with the
  existing client can select and call the correct dedicated operation for each
  of the five lifecycle tasks on the first attempt in at least 9 of 10 cases.
- **SC-010**: In concurrent-change tests, 100% of initially discovered tickets
  are refreshed before pairing, positions that become ineligible produce no new
  request, and positions opened after discovery produce zero outcomes.
- **SC-011**: A failed or transport-uncertain close-by pair is attempted exactly
  once per batch invocation, while a successfully partially closed position can
  be paired again using its refreshed remaining volume.
- **SC-012**: For every tested completed, partially completed, accepted/placed,
  rejected/failed, and unknown MT5 return code, the dedicated result reports the
  expected execution category while preserving the exact raw code; zero rejected,
  failed, or unknown outcomes are labeled as completed execution.

## Assumptions

- "Opening" includes immediate market orders and placement of pending orders;
  cancellation/removal of pending orders is outside this feature because it was
  not one of the requested lifecycle operations and remains possible through
  the generic send operation.
- "Modifying" includes both stop-loss/take-profit changes to an open position
  and editable changes to a pending order; these variants have distinct MT5
  action mappings but belong to one public modification capability.
- A close operation supports full or partial position closure and receives a
  current position snapshot or equivalent explicit values. It does not silently
  fetch account state, because that would add latency and introduce a second
  state snapshot before a financially consequential request.
- "Multiple close-by" automatically discovers and pairs all eligible opposite
  positions for one required symbol, restricted to one magic number when that
  optional filter is supplied. Eligible buys and sells are paired oldest-first,
  with ascending ticket as the equal-time tie-breaker. Processing remains
  sequential and non-atomic and exposes every pairing decision and outcome for
  auditability.
- Automatic discovery freezes the initially eligible ticket membership. Current
  state for those tickets is refreshed before each pair; newly opened tickets are
  excluded until a later invocation.
- Close-by is available only when MT5 account and position rules permit it,
  notably for opposite positions on the same symbol in hedging mode. The client
  validates only facts available from its inputs and preserves MT5's decision
  for current account state.
- MT5's official trade-action and request-field semantics remain the source of
  truth for operation mapping. A successful request submission is not assumed
  to prove trade execution; dedicated results expose call status and a separate
  execution status while preserving the raw trade return code.
- The existing generated contract, server `SendOrder` implementation, typed
  client result wrapper, deadline and cancellation behavior, logging, package
  targets, and test infrastructure are available and remain dependencies.
