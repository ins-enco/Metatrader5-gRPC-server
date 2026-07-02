# Feature Specification: C# Request Enum Types

**Feature Branch**: `003-csharp-request-enums`
**Created**: 2026-07-02
**Status**: Draft
**Input**: User description: "At C# client library. For every request callers must pass magic integers (action = 1, type = 5) with no compile-time safety, no IntelliSense, and no protection against invalid or mismatched values — errors only surface at runtime on the MT5 server. Add C# enum types (with explicit MT5 numeric values) for every request field that is semantically an MT5 enum but is currently exposed as a raw int — e.g. TradeRequest.Action, Type, TypeFilling, TypeTime, plus OrderCalc.action. Enum class provided separately."

## Clarifications

### Session 2026-07-02

- Q: How should the delivered C# enums be named in the public surface? → A: Verbatim MT5 names from the provided file (`ENUM_ORDER_TYPE.ORDER_TYPE_BUY`, `ENUM_TRADE_REQUEST_ACTIONS.TRADE_ACTION_DEAL`, `ENUM_ORDER_TYPE_FILLING`, `ENUM_ORDER_TYPE_TIME`), placed in the client library namespace rather than `MtApi5`.
- Q: Does scope include response-side reads (position/deal/transaction types) or stay request-only? → A: Request-only — the 6 request fields; response-side typing is a future follow-up.
- Q: Should `OrderCalc*.action` use the full shared `ENUM_ORDER_TYPE` or a restricted Buy/Sell-only enum? → A: Full shared `ENUM_ORDER_TYPE` (all 9 members); document that profit calc expects Buy/Sell rather than compile-enforcing it.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Build a Trade Request With Named Values (Priority: P1)

A C# developer building a trade request selects the trading operation, order
type, filling policy, and time-in-force policy from named, discoverable choices
instead of memorizing MT5 magic integers. The editor offers the valid choices,
and the compiler rejects a value that is not a legal option for that field.

**Why this priority**: This is the core value of the feature. Trade submission
is the highest-risk operation in the library, and passing the wrong integer to a
trade field (e.g. an order-type value in the action field) currently produces an
error only after the request reaches the MT5 server, after network round-trip,
potentially with financial consequence.

**Independent Test**: A developer writes code that constructs a trade request
using the named request-field values, without referencing any numeric literal,
and the request produced carries the same values a correct hand-written integer
request would have carried.

**Acceptance Scenarios**:

1. **Given** a developer is composing a trade request, **When** they set the
   trading operation, order type, filling policy, and time-in-force fields,
   **Then** each field accepts a named value from a set restricted to the legal
   MT5 options for that field and rejects values that belong to a different
   field's option set at author/compile time.
2. **Given** a developer selects a named value for a request field, **When** the
   request is sent to the server, **Then** the value transmitted equals the
   documented MT5 numeric value for that named option, so server behavior is
   identical to submitting that integer directly.
3. **Given** a developer is editing a request field in an IDE, **When** they
   invoke completion on that field, **Then** the available MT5 options for that
   field are listed by name.

---

### User Story 2 - Choose Buy/Sell for Calculation Requests (Priority: P2)

A C# developer requesting a margin or profit calculation specifies the operation
direction (buy or sell) using the same named order-type values used elsewhere,
rather than passing a raw integer whose meaning is documented only in a comment.

**Why this priority**: Calculation requests are common pre-trade checks. They
share the order-type concept with trade submission, so a consistent named
representation reduces mistakes and cognitive load, though a wrong value here is
lower risk than on a live trade because no order is placed.

**Independent Test**: A developer builds a margin request and a profit request
selecting the direction by name, and each request carries the documented MT5
order-type numeric value for that direction.

**Acceptance Scenarios**:

1. **Given** a developer is composing a margin or profit calculation request,
   **When** they set the operation-direction field, **Then** the field accepts a
   named order-type value and the transmitted value equals the documented MT5
   numeric value.
2. **Given** the order-type concept is represented as a named set, **When** it is
   used for a calculation request and for a trade request, **Then** both use the
   same named representation and numeric mapping.

---

### User Story 3 - Preserve Existing Integer-Based Code (Priority: P3)

A developer with existing code that already sets these request fields using raw
integers can adopt the new library version without being forced to rewrite that
code, and can migrate field-by-field at their own pace.

**Why this priority**: The library already ships and is consumed (including by
.NET Framework 4.8 applications per the existing client-library feature).
Breaking every existing caller that sets these fields would block adoption and
contradict the project's backward-compatible-addition posture.

**Independent Test**: A code sample that sets the affected fields using the prior
integer-based approach still compiles and produces the same request against the
new library version, and a second sample using the named values produces an
equivalent request.

**Acceptance Scenarios**:

1. **Given** existing caller code that sets an affected request field with an
   integer, **When** the code is compiled against the new library version,
   **Then** it continues to compile and produces the same transmitted value.
2. **Given** a caller mixes named values on some fields and integers on others,
   **When** the request is built and sent, **Then** all fields transmit their
   correct MT5 numeric values.

### Edge Cases

- A caller wants to set a request field to an MT5 numeric value that is not part
  of the currently defined named set (for example, a value MT5 introduces in a
  later terminal build). The library must not permanently prevent sending a valid
  future MT5 value.
- A server response or round-tripped request contains a numeric value in one of
  these fields that has no corresponding named value. Reading such a value must
  not throw or corrupt the value.
- Two fields share overlapping numeric ranges (e.g. an action value of `1` and an
  order-type value of `1` mean different things); the named representation must
  keep each field's option set distinct so a value intended for one field cannot
  be silently accepted as valid for another.
- The authoritative MT5 numeric values provided separately differ from any value
  assumed during drafting; the delivered named values MUST match the
  authoritative source.
- A .NET Framework 4.8 / `netstandard2.0` consumer uses the named values and must
  get the same behavior as a modern .NET consumer.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The C# client library MUST provide named, strongly typed value sets
  for every request field that is semantically an MT5 enumeration but is
  currently exposed to callers as a raw integer.
- **FR-002**: The initial set of covered request fields MUST include the trade
  request's trading-operation field, order-type field, filling-type field, and
  time-in-force field, and the margin and profit calculation requests'
  operation-direction (order-type) field.
- **FR-003**: Each named value MUST carry the explicit, documented MT5 numeric
  value it represents, and the value transmitted for a named selection MUST equal
  that MT5 numeric value so server-observed behavior is unchanged.
- **FR-004**: Each covered field MUST expose a value set restricted to the MT5
  options valid for that field, such that a value belonging to a different
  field's option set is rejected at author/compile time rather than at runtime.
- **FR-005**: The order-type concept that is shared between trade submission and
  the calculation requests MUST use a single consistent named representation and
  numeric mapping (`ENUM_ORDER_TYPE`, all nine members) across both uses. The
  margin and profit calculation `action` fields accept the full `ENUM_ORDER_TYPE`;
  documentation MUST state that profit calculation expects the Buy or Sell member,
  and this expectation is not compile-enforced.
- **FR-006**: The library MUST remain backward compatible for existing callers
  that set these fields with integers: such code MUST continue to compile and
  transmit the same values without modification.
- **FR-007**: The library MUST allow a caller to represent and transmit a valid
  MT5 numeric value that is not part of the currently defined named set, so that
  future MT5-introduced values are not blocked by the type change.
- **FR-008**: Reading a field that contains a numeric value with no corresponding
  named value MUST NOT throw or alter the underlying value.
- **FR-009**: The named values and their numeric mappings MUST be sourced from
  the authoritative MT5 enum definitions supplied for this feature
  (`specs/003-csharp-request-enums/Mt5Enums.cs`); the library MUST NOT invent
  numeric values or introduce undocumented sentinel values.
- **FR-012**: The delivered enum types MUST use the verbatim MT5 enum type and
  member names from the supplied source (e.g. `ENUM_ORDER_TYPE.ORDER_TYPE_BUY`,
  `ENUM_TRADE_REQUEST_ACTIONS.TRADE_ACTION_DEAL`), exposed within the client
  library's own namespace, so callers can cross-reference official MT5/MQL5
  documentation directly.
- **FR-010**: Documentation and usage examples for building trade, order-check,
  margin, and profit requests MUST demonstrate the named values for the covered
  fields.
- **FR-011**: The named value sets MUST be usable by all supported client target
  frameworks, including the `netstandard2.0` / .NET Framework 4.8 compatibility
  target that the client library already supports.

### Protocol and MT5 Contract Impact *(mandatory)*

- **Affected RPCs/Messages**: Request-side fields only —
  `TradeRequest.action`, `TradeRequest.type`, `TradeRequest.type_filling`,
  `TradeRequest.type_time` (reached through `OrderSendRequest.trade_request` and
  `OrderCheckRequest.trade_request`), and `OrderCalcMarginRequest.action` and
  `OrderCalcProfitRequest.action`. Response-side integer enum fields (e.g. deal
  type, position type, historical/open order type, market-book type,
  symbol filling mode) are out of scope for this feature.
- **Compatibility Decision**: Backward-compatible addition scoped to the C#
  client library only. The `.proto` contract is unchanged: the request fields
  remain `int32` on the wire, and the named value sets add a type-safe C# access
  path over those existing integer fields. There is no cross-language impact —
  the Python server and other-language clients are untouched, and no proto
  regeneration or field renumbering is required. Existing integer-based callers
  keep working.
- **MT5 Operation Mapping**: The covered fields map to MT5's trade-request action
  set, order-type set, order-filling-type set, and order-time-type set, and to
  the buy/sell order-type values used by margin and profit calculation. Named
  selections MUST resolve to the same MT5 numeric values MT5 expects; MT5 return
  codes and error payloads remain unchanged and continue to surface to callers.
- **Cross-Language Type Notes**: The underlying transmitted value remains an
  integer on the wire. Named values are a typed presentation of that integer.
  Values outside the named set MUST remain representable and round-trip without
  loss. No sentinel values are introduced.

### Key Entities *(include if feature involves data)*

- **Trade Action Value Set**: The named MT5 trading-operation options for the
  trade request's action field, each mapped to its MT5 numeric value.
- **Order Type Value Set**: The named MT5 order-type options, shared by the trade
  request's order-type field and by the margin and profit calculation
  operation-direction fields.
- **Order Filling Value Set**: The named MT5 order-filling-policy options for the
  trade request's filling field.
- **Order Time Value Set**: The named MT5 time-in-force options for the trade
  request's time field.
- **Covered Request Field**: A request message field, semantically an MT5 enum,
  currently exposed as a raw integer, that this feature gives a named typed
  representation.
- **Authoritative MT5 Enum Source**: The externally supplied definition of MT5
  enum names and their numeric values that the delivered named value sets must
  match.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of the request fields listed in FR-002 are settable using
  named values without any numeric literal in caller code.
- **SC-002**: For every named value, the transmitted numeric value equals the
  authoritative MT5 numeric value for that option (0 mismatches).
- **SC-003**: A value valid for one covered field but not for another cannot be
  assigned to the wrong field without a compile error, verified for each covered
  field.
- **SC-004**: 100% of existing caller code paths that set the covered fields via
  integers continue to compile and transmit identical values against the new
  library version (0 forced source changes).
- **SC-005**: A field can still be set to, and can still read back, an MT5 numeric
  value that has no named entry, without error or value change.
- **SC-006**: Documented examples for trade, order-check, margin, and profit
  requests each use the named values and either succeed against a test server or
  return the expected typed failure result.
- **SC-007**: The named values compile and behave identically on the
  `netstandard2.0` / .NET Framework 4.8 compatibility target and on a modern .NET
  target.

## Assumptions

- The authoritative MT5 enum names and numeric values will be supplied separately
  ("Enum class provided later"); the delivered named sets must match that source
  exactly, and drafting-time value guesses carry no authority.
- Scope is limited to request-side fields. Response-side integer enum fields are
  a possible later extension and are not included here.
- The feature is delivered as a backward-compatible addition; existing
  integer-based callers are not forced to change.
- Callers must retain a way to send valid MT5 numeric values not yet in the named
  set, because MT5 may add values in future terminal builds.
- The named value sets follow the existing C# client library's supported target
  frameworks, including `netstandard2.0`.
- Validation can use a test server, mocks, or contract fixtures rather than a live
  broker account, consistent with the existing client-library feature.
