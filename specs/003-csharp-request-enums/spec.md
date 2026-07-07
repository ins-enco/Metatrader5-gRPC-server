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

### Session 2026-07-02 (compatibility posture revised)

The initial C#-client-only, backward-compatible posture was reopened. The feature
now delivers **native protobuf enum-typed request fields** on the shared `.proto`
contract rather than a C#-only presentation layer over unchanged `int32` fields.

- Q: Scope of the contract change? → A: Update the `.proto`, regenerate both the
  C# client and Python (`mt5_grpc_proto`) bindings, and verify the Python server
  still reads the affected fields with identical numeric semantics.
- Q: How is proto3's mandatory zero-valued enum member handled, given
  `ENUM_TRADE_REQUEST_ACTIONS` has no MT5 zero and order-type/filling/time already
  use their real MT5 zero (BUY/FOK/GTC)? → A: Add `TRADE_ACTION_UNSPECIFIED = 0`
  to the actions enum only; the server MUST reject an unset/UNSPECIFIED action
  rather than executing it. The other three enums keep their real MT5 zero.
- Q: Versioning for this breaking public-contract change (integer callers no
  longer compile)? → A: Pre-1.0 breaking bump to `0.2.0` with a documented
  integer→enum migration and a coordinated proto contract-identity update.
- **Supersedes**: the earlier "backward-compatible addition scoped to the C#
  client library only; `.proto` unchanged" decision and the companion-property
  approach.

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

### User Story 3 - Migrate Existing Integer-Based Code (Priority: P3)

A developer with existing code that sets these request fields using raw integers
upgrades to the new library version, sees the now-invalid integer assignments
flagged by the compiler, and migrates each one to the named value (or an explicit
cast for a value with no named member) using the migration guide.

**Why this priority**: The library already ships and is consumed (including by
.NET Framework 4.8 applications per the existing client-library feature). Turning
the fields into enums is a deliberate breaking change, so the upgrade path must be
explicit, compiler-guided, and documented rather than silent.

**Independent Test**: A code sample written against the prior integer-based API is
upgraded following the migration guide, compiles against the new version, and
produces a request that transmits the same numeric values as before.

**Acceptance Scenarios**:

1. **Given** existing caller code that sets an affected request field with a raw
   integer, **When** it is compiled against the new library version, **Then** the
   compiler flags the integer assignment, and replacing it with the documented
   named value produces an identical transmitted value.
2. **Given** a caller needs a numeric value with no named member, **When** they
   follow the migration guide, **Then** an explicit `(EnumType)value` cast lets
   them set and transmit that value without error.

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
- **FR-006**: This is a breaking change to the request field types: the covered
  fields become native protobuf enum types, so existing C# code that assigns raw
  integers to them will no longer compile. The library MUST ship a documented
  integer→enum migration path (named values, or an explicit `(EnumType)value` cast
  for values with no named member) and MUST NOT change the transmitted numeric
  value for an equivalent selection. The wire encoding stays varint-compatible so
  no data or wire migration is required.
- **FR-007**: The library MUST allow a caller to represent and transmit a valid
  MT5 numeric value that is not part of the currently defined named set, so that
  future MT5-introduced values are not blocked by the type change. (proto3 enums
  are open and preserve unknown numeric values.)
- **FR-008**: Reading a field that contains a numeric value with no corresponding
  named value MUST NOT throw or alter the underlying value.
- **FR-009**: The named values and their numeric mappings MUST be sourced from
  the authoritative MT5 enum definitions supplied for this feature
  (`specs/003-csharp-request-enums/Mt5Enums.cs`); the library MUST NOT invent
  numeric values or introduce undocumented sentinel values, with the single
  documented exception of `TRADE_ACTION_UNSPECIFIED = 0`, required to satisfy
  proto3's mandatory zero-valued member for `ENUM_TRADE_REQUEST_ACTIONS` (which
  has no MT5-defined zero). This sentinel MUST be documented and treated as
  invalid input (see FR-014).
- **FR-012**: The delivered enum types MUST use the verbatim MT5 enum type and
  member names from the supplied source (e.g. `ENUM_ORDER_TYPE.ORDER_TYPE_BUY`,
  `ENUM_TRADE_REQUEST_ACTIONS.TRADE_ACTION_DEAL`). Because the enums are now
  defined in the shared `.proto` contract, they are generated into the protobuf
  package namespace (`metatrader.v1`, surfaced in C# as `Metatrader.V1`) rather
  than a hand-written client namespace; the verbatim names still let callers
  cross-reference official MT5/MQL5 documentation directly.
- **FR-013**: The affected request fields MUST be defined as protobuf enum types
  in the `.proto` contract, and all in-repo language bindings (C# client and the
  `mt5_grpc_proto` Python package) MUST be regenerated from the updated contract.
  The Python server MUST continue to read the affected fields with identical
  numeric semantics.
- **FR-014**: An unset or `TRADE_ACTION_UNSPECIFIED` trade-request action MUST be
  rejected with a structured error rather than executed, preserving trade safety
  for the default/zero case.
- **FR-015**: The shared `ENUM_ORDER_TYPE` MUST be defined once in a shared proto
  and imported by both the trade request and the order-calculation messages, so
  the type identity and numeric mapping are the same across all uses.
- **FR-016**: The change MUST be released as a coordinated version bump — the C#
  package to `0.2.0` with a matching proto contract-identity update — accompanied
  by migration documentation.
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
- **Compatibility Decision**: Breaking, coordinated contract change. The affected
  request fields change type from `int32` to native protobuf `enum` in the shared
  `.proto`. Field numbers are preserved and the wire encoding stays varint (proto3
  enums encode identically to `int32`), so there is no data or wire migration and
  unknown/future numeric values still round-trip (proto3 open-enum semantics).
  Source compatibility does break: existing C# integer assignments no longer
  compile and MUST migrate to named values or explicit enum casts. All in-repo
  bindings (C# client and the `mt5_grpc_proto` Python package) are regenerated and
  the Python server is re-verified. Released as `0.2.0` (pre-1.0 breaking) with a
  documented migration and a proto contract-identity bump. This supersedes the
  prior C#-only, `.proto`-unchanged decision.
- **MT5 Operation Mapping**: The covered fields map to MT5's trade-request action
  set, order-type set, order-filling-type set, and order-time-type set, and to
  the buy/sell order-type values used by margin and profit calculation. Named
  selections MUST resolve to the same MT5 numeric values MT5 expects; MT5 return
  codes and error payloads remain unchanged and continue to surface to callers.
- **Cross-Language Type Notes**: The transmitted value remains a varint on the
  wire, identical to the prior `int32` encoding, so cross-language
  interoperability and any existing serialized data are unaffected. Fields are now
  typed as protobuf enums; proto3 open-enum semantics keep values outside the
  named set representable and round-tripping without loss in every language. The
  only added value is the documented `TRADE_ACTION_UNSPECIFIED = 0` sentinel that
  proto3 requires for `ENUM_TRADE_REQUEST_ACTIONS`; it denotes "no action set" and
  is rejected by the server (FR-014). In Python, protobuf enum fields surface as
  plain integers, so the server reads them with unchanged semantics.

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
- **SC-004**: The migration guide covers 100% of the covered fields, and every
  documented migration step converts a prior integer assignment to a named value
  that transmits the identical numeric value (0 value changes across the
  migration).
- **SC-005**: A field can still be set to, and can still read back, an MT5 numeric
  value that has no named entry, without error or value change.
- **SC-006**: Documented examples for trade, order-check, margin, and profit
  requests each use the named values and either succeed against a test server or
  return the expected typed failure result.
- **SC-007**: The named values compile and behave identically on the
  `netstandard2.0` / .NET Framework 4.8 compatibility target and on a modern .NET
  target.
- **SC-008**: After regeneration, the Python server reads each covered field with
  the same numeric value it read under the `int32` contract, verified by a
  server-side contract or integration check.
- **SC-009**: A trade request submitted with an unset or `TRADE_ACTION_UNSPECIFIED`
  action returns a structured error and places no order.

## Assumptions

- The authoritative MT5 enum names and numeric values will be supplied separately
  ("Enum class provided later"); the delivered named sets must match that source
  exactly, and drafting-time value guesses carry no authority.
- Scope is limited to request-side fields. Response-side integer enum fields are
  a possible later extension and are not included here.
- The feature is delivered as a deliberate breaking change to the request field
  types; existing integer-based callers must migrate to named values (or explicit
  casts), guided by the compiler and a migration document, and released as a
  coordinated `0.2.0` bump.
- Callers must retain a way to send valid MT5 numeric values not yet in the named
  set, because MT5 may add values in future terminal builds; proto3 open-enum
  semantics and explicit casts provide this.
- The named value sets follow the existing C# client library's supported target
  frameworks, including `netstandard2.0`.
- Validation can use a test server, mocks, or contract fixtures rather than a live
  broker account, consistent with the existing client-library feature.
