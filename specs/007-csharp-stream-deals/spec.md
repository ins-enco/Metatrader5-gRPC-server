# Feature Specification: C# Client StreamDeals Surface

**Feature Branch**: `009-csharp-stream-deals`
**Spec Directory**: `specs/007-csharp-stream-deals`
**Created**: 2026-09-07
**Status**: Draft
**Input**: User description: "Expose the server's new StreamDeals server-streaming RPC in the C# client library (mt5_grpc_client_csharp), so a .NET consumer can read a large deal history without aborting the server."

## Context

`TradeHistoryService.StreamDeals` already exists server-side (branch `feat/stream-deals`,
commit 95e3317): `protos/deal.proto` declares it beside `GetDeals` and adds
`optional uint32 chunk_size = 5`; `mt5_grpc_server/imp/deals_history.py` shares filter
logic between both RPCs through `_fetch_deals`; Python bindings are regenerated. **No
proto contract change and no server behaviour change are in scope here** — the contract
and the implementation are already correct. What *is* in scope beyond the client is a
coordinated `0.4.0` release of the Python packages, so that the new RPC is gated by a
version a consumer can check rather than by a commit hash (see FR-015 and FR-016).

The C# client library was not updated. `Mt5GrpcClient` exposes only `GetDealsAsync`
(`src/MetaTrader.Grpc.Client/Mt5GrpcClient.Trading.cs:140`), so a .NET consumer has no
way to reach the streaming RPC. A consumer polling `GetDeals` for an account with ~1600
deals (~121 KB serialised) terminates the server process: the response exceeds a single
socket write and grpcio's Windows IOCP endpoint aborts
(`windows_endpoint.cc:363 Check failed: result.bytes_transferred == buffer_->Length()`,
unhandled exception `c0000409`). Docker `restart: unless-stopped` restarts the server,
the client repeats the call, and it aborts again — a crash loop for as long as that
account is polled. Production evidence over 8 minutes: 6 of 28 `GetDeals` calls returned
1571 deals and **all 6** aborted the server; calls returning 0–982 deals (~75 KB) never
did.

C# bindings are generated at build time by Grpc.Tools from `protos/*.proto`, so the
generated `StreamDeals` stub and `DealsRequest.ChunkSize` become available on the next
rebuild without hand-written contract work.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Read a large deal history without aborting the server (Priority: P1)

An integrator whose MT5 account holds several thousand closed deals needs the full
history in their .NET application. Today the only available call kills the server and
puts it into a restart loop. They call a chunk-level streaming surface instead and
consume it with `await foreach`, receiving the history as a sequence of bounded
messages. The server stays up.

**Why this priority**: This is the entire reason the feature exists. It is the only
story that removes the production crash loop, and it is the minimum viable slice — with
just this, an affected consumer has a working, safe path to their deal history.

**Independent Test**: Point the client at an in-process test server that yields a known
multi-chunk deal sequence for a given filter. Enumerate the surface with `await foreach`,
concatenate the chunks, and assert the deals, their order, and their tickets match the
expected sequence exactly, with the call completing normally.

**Acceptance Scenarios**:

1. **Given** a history of 1571 deals served as 4 chunks, **When** the caller enumerates
   the streaming surface, **Then** 4 items are yielded, their concatenation contains all
   1571 deals in server order with the same tickets, the enumeration completes normally,
   and the server process is still running afterwards.
2. **Given** an account whose filtered history is empty, **When** the caller enumerates
   the streaming surface, **Then** zero items are yielded, the enumeration completes
   normally, and no error is raised.
3. **Given** the server responds with a single message carrying only an error (an MT5
   failure or an invalid request), **When** the caller enumerates the surface, **Then**
   enumeration terminates by raising the library's client exception whose error carries
   the mapped MT5 error code and message, and no deal items are yielded.
4. **Given** a stream in progress, **When** the caller cancels the supplied cancellation
   token, **Then** enumeration terminates promptly with the library's cancellation error
   and the underlying call is released.
5. **Given** a caller-supplied deadline, **When** the deadline elapses mid-stream,
   **Then** enumeration terminates promptly with the library's deadline error and the
   underlying call is released.
6. **Given** the same filters, **When** the caller reads through the streaming surface
   and through the existing single-response call against a history small enough for both,
   **Then** both yield the same deals, in the same order, with the same tickets.

---

### User Story 2 - Reach the fix from .NET Framework 4.8 on the native channel (Priority: P2)

Most consumers of this library run on .NET Framework 4.8 / Windows 10 and connect
through the legacy native channel (`Mt5GrpcClientFactory.CreateCore`) because
`WinHttpHandler` lacks HTTP/2 on Windows 10. The streaming surface must work for them,
not only for modern hosts using `Mt5GrpcClientFactory.Create`.

**Why this priority**: Without this the fix does not reach the population that hit the
crash. It is second only to the surface itself, and it is a distinct slice: the surface
can be complete and still be unusable on the channel most affected consumers use.

**Independent Test**: Exercise a multi-chunk read from a .NET Framework host over a
channel built by `CreateCore`, and separately over one built by `Create`, asserting
identical concatenated results from both. Verify the library's `netstandard2.0` and
`net472` outputs both expose the surface.

**Acceptance Scenarios**:

1. **Given** a client built with `Mt5GrpcClientFactory.CreateCore`, **When** a caller
   reads a multi-chunk history, **Then** all chunks arrive in order and the concatenated
   result equals the result obtained over a client built with
   `Mt5GrpcClientFactory.Create`.
2. **Given** the published package, **When** a `net472`/`net48` consumer and a
   `netstandard2.0` consumer each reference it, **Then** both can reference and call the
   streaming surface and the chunk-size field with no additional package reference.
3. **Given** a stream over the native channel, **When** the caller cancels mid-stream,
   **Then** enumeration terminates promptly and the call is released, matching the
   behaviour on the modern channel.

---

### User Story 3 - Get the whole history in one call when memory allows (Priority: P3)

A caller who genuinely needs the entire history materialised in memory — a one-off
reconciliation job, a report — wants a near drop-in replacement for the existing
single-response call that is safe for the server. They call a convenience surface that
consumes the stream internally and hands back the concatenated deals through the
library's existing result conventions.

**Why this priority**: Real convenience for a legitimate use case, and the smallest
migration step for existing code, but it re-introduces the unbounded client-side memory
that the chunk-level surface avoids. Deliverable after the safe path exists.

**Independent Test**: Against the same in-process multi-chunk test server, call the
convenience surface and assert a successful result whose deals equal the concatenation of
all chunks in order; then make the server emit an error-only message and assert a failure
result carrying the mapped error.

**Acceptance Scenarios**:

1. **Given** a history served as several chunks, **When** the caller invokes the
   convenience surface, **Then** it returns a successful result whose deals are the
   ordered concatenation of every chunk, with the same tickets as the single-response
   call for the same filters.
2. **Given** an empty filtered history, **When** the caller invokes the convenience
   surface, **Then** it returns a successful result containing zero deals and no error.
3. **Given** the stream fails part-way (in-band error message or transport fault),
   **When** the caller invokes the convenience surface, **Then** it returns a failure
   result carrying the mapped error and no partial deal collection, rather than raising.
4. **Given** the documentation, **When** a caller evaluates this surface, **Then** its
   unbounded-memory cost and the recommendation to prefer the chunk-level surface for
   large histories are stated at the point of use.

---

### User Story 4 - Adopt the incremental polling pattern (Priority: P4)

An operator running a polling loop currently re-reads the whole history every cycle.
Documentation and both runnable examples show the intended pattern: backfill once through
the streaming surface, then fetch incrementally with `date_from` set to the last deal
already held. The closed part of a history never changes, so re-reading all of it every
cycle is both what triggers the crash and pure waste.

**Why this priority**: Guidance, not capability — the earlier stories already make the
crash avoidable. But without it consumers keep the wasteful pattern and stay near the
failure regime, so it ships with the feature rather than after it.

**Independent Test**: Read the README section and run both example projects; confirm each
demonstrates a backfill pass followed by an incremental fetch anchored on the last held
deal, and that a cycle with nothing new transfers no deals.

**Acceptance Scenarios**:

1. **Given** the README and both example projects, **When** a consumer follows them,
   **Then** they see a backfill pass through the streaming surface followed by incremental
   fetches anchored on the last held deal's timestamp.
2. **Given** an incremental fetch anchored on the last held deal, **When** no new deals
   have occurred, **Then** the call returns zero new deals and does not re-transfer
   history already held.
3. **Given** the documentation for the existing single-response call, **When** a consumer
   reads it, **Then** it states the large-history failure mode and points to the streaming
   surface.

---

### Edge Cases

- **Empty filtered history**: the stream completes with zero chunks and no error — it MUST
  NOT be reported as a failure or as an empty-with-error result.
- **Error-only message**: a message carrying only an error (MT5 failure or invalid request)
  terminates the stream through the library's exception, matching the existing
  single-response error behaviour; the convenience surface converts it to a failure result.
- **Chunk size near the server cap**: production evidence puts a deal at roughly 77 bytes
  serialised, and the abort threshold somewhere between ~75 KB (observed safe) and ~121 KB
  (observed fatal). The server cap of 1000 deals per chunk is therefore ~77 KB — already
  inside the uncertain band — while the server default of 500 (~38 KB) is comfortably clear
  of it. Requesting a chunk size at or near the cap can re-enter the failure regime the
  feature exists to escape; guidance MUST say so, and the client MUST NOT raise a caller's
  chunk size or supply one on their behalf.
- **Chunk size unset, zero, or above the cap**: unset means the server default; the server
  owns clamping. The client transmits what the caller set, transmits nothing when the caller
  set nothing, and reports the server's behaviour rather than pre-empting it.
- **Server predates StreamDeals**: the call fails as an unimplemented method surfaced through
  the library's mapped error. There is no automatic client-side fallback to the
  single-response call — a silent fallback would restore the crash.
- **Cancellation or deadline mid-stream**: terminates promptly, releases the call, leaks no
  pending call or running enumeration.
- **Enumeration abandoned early**: a caller who breaks out of `await foreach` before the
  stream ends releases the call rather than leaving it open.
- **Multiple concurrent streams on one client**: each is independent; one failing or being
  cancelled does not disturb another.
- **Existing callers**: code calling the existing single-response operation compiles and
  behaves exactly as before after upgrading.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The client MUST expose a chunk-level surface for
  `TradeHistoryService.StreamDeals` that maps 1:1 to the server stream, yielding one item
  per server message and consumable with `await foreach`, accepting the same request
  message as the existing single-response operation plus an optional deadline and a
  cancellation token.
- **FR-002**: The chunk-level surface MUST follow the conventions already established by
  the library's existing streaming subscription: an in-band error message and a transport
  fault both terminate enumeration by raising the library's client exception carrying the
  mapped error, with the same error-mapping and logging behaviour as the existing unary and
  streaming paths.
- **FR-003**: An empty filtered history MUST complete the stream normally with zero items
  and no error.
- **FR-004**: The client MUST expose a convenience surface that consumes the whole stream
  and returns the ordered concatenation of all chunks' deals through the library's existing
  result conventions — success carrying the deals, failure carrying the mapped error and no
  partial collection — so that it is a near drop-in for the existing single-response
  operation.
- **FR-005**: A caller-supplied chunk size MUST be transmitted verbatim. The client MUST NOT
  set, raise, lower, clamp, or default the chunk size on the caller's behalf; when the caller
  supplies none, none is transmitted and the server's default applies.
- **FR-006**: The existing `GetDealsAsync` operation MUST remain, with its exact current
  signature and behaviour. Neither operation may automatically fall back to the other.
- **FR-007**: For identical filters, the ordered concatenation of streamed chunks MUST
  contain the same deals, in the same order, with the same tickets, as the single-response
  operation. This MUST hold for each of the three request filter forms (time filter, order
  ticket, position ticket) and with and without the symbol group filter.
- **FR-008**: Cancelling the supplied token or exceeding a supplied deadline MUST terminate
  enumeration promptly, surface the library's corresponding mapped error, and release the
  underlying call without hanging or leaking it. Abandoning enumeration early MUST also
  release the call.
- **FR-009**: Both surfaces MUST be usable from both library target frameworks
  (`netstandard2.0` and `net472`) and through both channel constructors —
  `Mt5GrpcClientFactory.Create` and `Mt5GrpcClientFactory.CreateCore` — with identical
  observable results. The native-channel path is required, not optional.
- **FR-010**: Streaming calls MUST log like the existing unary and streaming invokers:
  bounded output that does not grow with history size, sufficient to diagnose request,
  response, and MT5 failures, and carrying no credentials or account secrets.
- **FR-011**: Documentation and both runnable examples MUST present the intended usage
  pattern — backfill once through the streaming surface, then fetch incrementally anchored
  on the last held deal's timestamp — and MUST state: the unbounded-memory cost of the
  convenience surface, the server's chunk-size default and cap, the risk of requesting a
  chunk size near the cap, and the large-history failure mode of the existing
  single-response operation.
- **FR-012**: The generated client surface MUST be covered by contract verification
  asserting that `TradeHistoryService` exposes `StreamDeals` and that the deals request
  message exposes the chunk-size field, with the streaming-service catalog updated to
  include it while leaving the unary contract inventory unchanged.
- **FR-013**: Behaviour MUST be covered by in-process streaming tests built on the existing
  test-server fixtures, requiring no live broker, covering: multi-chunk delivery and
  concatenation order, empty history, an error-only response, cancellation mid-stream, and
  chunk-size transmission including the server's clamping behaviour. Reachability from .NET
  Framework MUST be covered by the existing compatibility test project.
- **FR-014**: The client release MUST ship an additive minor version bump (`5.0.2` to
  `5.1.0`), a README section, a CHANGELOG entry, and MIGRATION notes. The authored
  compatibility metadata MUST be updated to `<ProtoContractIdentity>protos-007-stream-deals</ProtoContractIdentity>`
  and `<TestedServerVersionRange>[0.4.0,1.0.0)</TestedServerVersionRange>`, and both values
  MUST be quoted verbatim in the packed README and in `PackageReleaseNotes` so the existing
  documentation drift guard keeps passing.
- **FR-015**: The streaming surface MUST be gated by a server version a consumer can check
  before calling, not by a commit hash. The `mt5_grpc_proto` and `mt5_grpc_server` packages
  MUST be released as `0.4.0` — the first version whose contract includes `StreamDeals` and
  `chunk_size` — and the client MUST declare `[0.4.0,1.0.0)` as its tested server range. A
  `0.3.0` server MUST NOT be advertised as supporting the streaming surface, because the
  `0.3.0` packages shipped before the RPC existed.
- **FR-016**: The `0.4.0` package bump MUST be performed with the repository's existing
  release tooling (`bump_version.py minor`, which updates `mt5_grpc_server/setup.py` and
  `mt5_grpc_proto/setup.py` together) so the release is reproducible, and the root
  `CHANGELOG.md` MUST record `StreamDeals` and `chunk_size` as the additive contract change
  distinguishing `0.4.0` from `0.3.0`. The client MUST NOT be published claiming
  `[0.4.0,1.0.0)` before a `0.4.0` server and proto release satisfying that range exists.

### Protocol and MT5 Contract Impact *(mandatory)*

- **Affected RPCs/Messages**:
  `TradeHistoryService.StreamDeals(DealsRequest) returns (stream DealsResponse)` and
  `DealsRequest.chunk_size` (field 5, `optional uint32`) — both already present in
  `protos/deal.proto`. `DealsResponse` (`repeated Deal deals = 1`,
  `optional Error error = 2`), `Deal`, and `TradeHistoryService.GetDeals` are untouched.
  **This feature adds no proto changes**; it consumes the existing contract through C#
  bindings that Grpc.Tools regenerates from `protos/*.proto` at build time.
- **Compatibility Decision**: Backward-compatible additive change. No field numbers are
  added, reused, renamed, or removed; no existing signature or wire behaviour changes; the
  existing single-response operation is preserved verbatim. Server behaviour, target
  frameworks, and dependency groups are unchanged. Two coordinated version moves, per the
  constitution's requirement that package and contract versioning be coordinated:
  - **Client** `MetaTrader.Grpc.Client`: `5.0.2` to `5.1.0` (additive minor).
  - **Python packages** `mt5_grpc_proto` and `mt5_grpc_server`: `0.3.0` to `0.4.0`
    (additive minor). This is a release-only move — the proto contract and server
    implementation already carry `StreamDeals`; `0.4.0` is what makes that state
    identifiable to a consumer.
  - The authored `<ProtoContractIdentity>` moves from `protos-005-trade-transaction-events`
    to `protos-007-stream-deals`, which names the proto contract state rather than the
    consuming client feature — the identity is a language-neutral contract marker, so the
    C# qualifier in this spec's directory name is deliberately dropped from it.
  - `<TestedServerVersionRange>` moves from `[0.3.0,1.0.0)` to `[0.4.0,1.0.0)`, narrowing
    the advertised floor to the first server that actually implements the RPC.
- **MT5 Operation Mapping**: Both RPCs read closed trade history through the same server
  helper (`_fetch_deals`), which wraps the MT5 deals-history query. Filter semantics, symbol
  group filtering, MT5 error codes and messages, deal tickets, order and position
  identifiers, and timestamp handling (`time` in Unix seconds, `time_msc` in milliseconds)
  are those of the existing single-response operation. MT5 failures and invalid requests are
  reported as one message carrying only the error, identically to the single-response
  operation. The client adds no validation, coercion, filtering, or reordering of its own.
- **Cross-Language Type Notes**: `chunk_size` is `optional`, so presence is meaningful and
  distinct from a transmitted zero — unset must remain unset on the wire so the server
  applies its own default. The `filter` oneof keeps exactly one active filter form. Chunk
  ordering is the server's iteration order; deals within a chunk keep their repeated-field
  order, and concatenation across chunks is the caller-visible total order. No sentinel
  values are introduced.

### Key Entities

- **Deal chunk**: one streamed response message carrying a bounded slice of the filtered
  deal history, plus an optional error. Zero or more chunks make up a complete read; an
  error-carrying chunk is terminal.
- **Deal history read**: a logical read defined by one request (filter form, optional symbol
  group, optional chunk size), realised either as a chunk sequence or as one concatenated
  collection.
- **Read anchor**: the timestamp of the newest deal a consumer already holds, used as the
  lower bound of the next incremental fetch so the unchanging closed history is not
  re-transferred.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An account whose filtered history holds at least 1600 deals can be read
  end-to-end through the new surface 20 consecutive times with zero server process
  terminations and zero server restarts — against a baseline where 6 of 28 single-response
  reads of that history aborted the server.
- **SC-002**: For the same filters, the deals obtained by concatenating streamed chunks are
  identical in count, order, and ticket sequence to those from the single-response
  operation, verified for all three filter forms and for the symbol group filter both set
  and unset.
- **SC-003**: An empty filtered history yields zero chunks, no error, and normal completion
  in 100% of runs.
- **SC-004**: Cancelling mid-read returns control to the caller within 1 second and leaves
  no pending call or running enumeration.
- **SC-005**: Every existing caller of the single-response operation upgrades with no source
  change: the existing examples, tests, and benchmarks compile and pass unmodified against
  the new version.
- **SC-006**: A .NET Framework 4.8 consumer on the native channel completes a multi-chunk
  read with results identical to the modern channel, verified for both library target
  frameworks.
- **SC-007**: Peak client memory during a chunk-level read scales with one chunk rather than
  the whole history — reading a 1600-deal history retains no more than roughly one chunk's
  worth of deals at a time — while the convenience surface's whole-history retention is
  documented as the explicit trade-off.
- **SC-008**: After a one-time backfill, a steady-state incremental cycle with no new
  activity transfers zero deals, and one with N new deals transfers N — no re-reading of
  closed history.
- **SC-009**: A consumer can determine whether their server supports the streaming surface
  before making a call, from declared version metadata alone: the client declares
  `[0.4.0,1.0.0)`, a `0.4.0` server and proto release satisfying it exists, and no `0.3.0`
  server is described anywhere in the package's documentation or metadata as supporting the
  surface.
- **SC-010**: The compatibility values authored in the project file appear verbatim in the
  packed README and release notes, so the existing documentation drift guard passes with no
  waiver.

## Assumptions

- **No proto or server *behaviour* work.** `protos/deal.proto` and `mt5_grpc_server` are
  already complete and correct for this feature; C# bindings regenerate at build time, so no
  hand-written contract or generated-code edits are needed. The one non-client change in
  scope is the coordinated `0.4.0` version bump and its changelog entry (FR-015, FR-016) —
  a release action, not a code change. This is a deliberate widening of the
  "client library only" scope in the feature description, accepted because the alternative
  leaves `0.3.0` naming two different contracts and makes the compatibility gate unverifiable.
- **Release ordering is a dependency, not a task here.** The client `5.1.0` publish depends
  on the `0.4.0` Python release existing first. If the two cannot ship together, the client
  work can still be completed and verified — only the publish step is gated.
- **Surface naming mirrors the RPC.** The chunk-level surface is assumed to be named after
  the RPC it maps to (as the existing streaming subscription is), and the convenience
  surface named to read as a whole-history variant of the existing operation. Names are a
  public-API commitment on a published package, so they are called out here to be settled at
  spec time rather than after release.
- **Convenience surface discards partials on failure.** To be a genuine drop-in for the
  existing single-response operation, the convenience surface reports failure through the
  library's result type rather than raising, and returns no partial deal collection
  alongside an error. Callers who need partial progress use the chunk-level surface.
- **No client-side chunk-size policy.** The client supplies no default chunk size and applies
  no cap of its own; the server owns the default (500) and the cap (1000). The client's
  contribution is documentation, including that a chunk size near the cap approaches the
  ~77 KB/1000-deal size band where aborts were observed.
- **No new test infrastructure.** The existing in-process test-server and streaming fixtures
  are sufficient; no live broker, MT5 terminal, or Wine environment is needed for the
  automated tests.
- **Per-deal size estimate.** The ~77 bytes/deal figure and the safe/fatal band are derived
  from the reported production observations (1571 deals ≈ 121 KB fatal, 982 deals ≈ 75 KB
  safe), not from a controlled measurement. Chunk-size guidance is calibrated to that
  evidence and should be restated if a controlled measurement disagrees.
- **Server abort mode is out of scope.** Making the server resilient to an oversized single
  write is a server concern; this feature only gives C# callers a path that stays within the
  working regime.
