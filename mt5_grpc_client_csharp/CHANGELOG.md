# Changelog

## 5.1.0

### Added

- Added `StreamDealsAsync`, an `IAsyncEnumerable<DealsResponse>` over the new
  server-streaming `TradeHistoryService.StreamDeals` RPC. One yielded item per
  server message, in server order; consume it with `await foreach`. Retention is
  one chunk at a time, so client memory tracks a chunk rather than the history.
  Cancellation, an elapsed deadline and abandoning the enumeration early all
  release the underlying call.
- Added `GetAllDealsAsync`, which consumes that stream internally and returns the
  ordered concatenation as one synthesized `DealsResponse`. It returns
  `Task<Mt5GrpcResult<DealsResponse>>` — the **same type** `GetDealsAsync`
  returns — so migrating is a one-token rename that leaves `IsSuccess`, `Error`
  and `Value.Deals` working unchanged. Failures are returned rather than thrown,
  and no partial collection is ever returned alongside an error.
- Chunk size is server policy and pass-through only: `chunk_size` (field 5) unset
  means the server's default of 500, and a larger value is clamped to its cap of
  1000. The client never sets, raises, lowers, clamps, defaults or clears the
  field. Prefer the default — a chunk at the 1000 cap is roughly 77 KB, inside the
  band where server aborts were observed.
- Added a README "Deal history" section, a `GetAllDealsAsync` migration section in
  [MIGRATION.md](./MIGRATION.md), and the backfill-then-incremental pattern in
  both runnable examples (the `net48` one over the native `Grpc.Core` channel).
- Added no-broker contract coverage for the new surface — multi-chunk
  concatenation, empty history, in-band error, transport `Unimplemented`,
  cancellation, deadline, release-on-abandon, verbatim chunk-size transmission
  including an above-cap value, concurrent streams, bounded logging, and
  unary/streaming parity across all three filter forms with `group` set and unset.

### Changed

- `GetDealsAsync` gains XML documentation describing its large-history failure
  mode and pointing to `StreamDealsAsync`. **Its signature and behaviour are
  unchanged.**

### Compatibility

- Additive release. **Requires a server at `0.4.0` or later**: `StreamDeals` does
  not exist before it, and an older server fails the call with gRPC status
  `Unimplemented`. There is **no automatic fallback in either direction** between
  `StreamDeals` and `GetDeals` — a fallback would send exactly the oversized single
  response that terminates the server.
- Proto contract identity moves to `protos-007-stream-deals` and the tested server
  range to `[0.4.0,1.0.0)`. No `.proto` file, generated binding or server behaviour
  changed; `GetDeals`, `DealsResponse` and `Deal` are untouched, and no field
  number was added, reused, renamed or removed.
- **No new dependency and no target-framework change.** `IAsyncEnumerable` on
  `netstandard2.0` continues to come from the already-referenced
  `Microsoft.Bcl.AsyncInterfaces`, so a `net48` or `netstandard2.0` consumer calls
  both new surfaces with no additional package reference.
- Every existing caller compiles and passes unmodified — the two examples, all
  four test projects and the benchmarks.

## 5.0.0

### Added

- Added `OpenOrderAsync`, `ClosePositionAsync`, `CloseOrderAsync`,
  `ModifyTradeAsync`, and `ClosePositionByAsync` intent-focused operations. Each
  builds a fresh request, sends at most once, never retries, and preserves the
  raw order-send response together with a conservative execution status.
- `ClosePositionAsync` accepts only a position ticket and optional volume, then
  uses one position lookup and one symbol-info lookup to derive the DEAL fields.
  `CloseOrderAsync` accepts only a pending-order ticket and maps REMOVE without
  a lookup.
- Added deterministic `ClosePositionsByAsync` for one symbol and optional magic
  scope. It freezes initial ticket membership, refreshes before each FIFO pairing
  decision, processes BUY-primary close-by pairs sequentially, never retries an
  uncertain pair, and retains attempted, unattempted, withheld, missing,
  ineligible, and unmatched outcomes.
- Added lifecycle examples, compatibility coverage, structured bounded logging,
  and scripted tests that require no live broker.

### Compatibility

- Additive C# client-only release. `SendOrderAsync`, `protos/trade.proto`,
  `protos/position.proto`, `protos/symbol_info.proto`, generated bindings, Python packages, server behavior,
  package targets, dependency groups, proto contract identity
  `protos-005-trade-transaction-events`, and tested server range
  `[0.3.0,1.0.0)` are unchanged.

## 0.3.0

### Added

- **Trade transaction event streaming** — `Mt5GrpcClient.SubscribeTradeTransactionsAsync`
  returns an `IAsyncEnumerable<TradeTransactionEvent>` mapping 1:1 to the new
  `TradeEventsService.SubscribeTradeTransactions` server stream (the first streaming
  RPC in the contract). A `TradeTransactionSubscription` event wrapper
  (`SubscribeTradeTransactions`) layers `TransactionReceived` / `Completed` /
  `Faulted` events over the sequence for subscribe-style ergonomics.
- Stream faults (transport or in-band `Error`) surface as `Mt5GrpcClientException`
  whose `Error` carries the mapped `Mt5GrpcError`, so consumers can resubscribe from
  the last received `TimeMsc`.
- Added `Microsoft.Bcl.AsyncInterfaces` so `IAsyncEnumerable<T>` is available on
  `netstandard2.0`; internal `Mt5GrpcStreamingInvoker` mirrors the unary invoker's
  error mapping and logging.

### Changed

- Version `0.3.0`; proto contract identity `protos-005-trade-transaction-events`;
  tested server range `[0.3.0,1.0.0)`. Purely additive — no existing RPC, message,
  field, or field number changed.

## Unreleased

### Distribution

- The package is now consumed from the organization's **GitHub Packages** NuGet
  registry (`https://nuget.pkg.github.com/ins-enco/index.json`): add the source
  with a `read:packages` token and a single `PackageReference` — all runtime
  dependencies resolve automatically and no protobuf/gRPC code generation runs in
  the consumer. See the README "Install from GitHub Packages" section.
- Publishing is **tag-triggered and reproducible**: pushing a client-scoped tag
  `csharp-client-v<X.Y.Z>` runs the `csharp-client-publish` workflow, which builds,
  tests, runs generated-binding + package-metadata gates, verifies the tag matches
  `<Version>`, packs deterministically, and pushes with the CI `GITHUB_TOKEN`.
  Re-publishing an existing version is rejected (HTTP 409); versions are immutable.
- No proto/contract, wire, or MT5 behavior change — distribution only.

## 0.2.0

### Breaking

- Request fields that MT5 treats as enumerations are now **native protobuf enum
  types** instead of `int32`. Affected: `TradeRequest.Action`
  (`ENUM_TRADE_REQUEST_ACTIONS`), `TradeRequest.Type` (`ENUM_ORDER_TYPE`),
  `TradeRequest.TypeFilling` (`ENUM_ORDER_TYPE_FILLING`), `TradeRequest.TypeTime`
  (`ENUM_ORDER_TYPE_TIME`), and `OrderCalcMarginRequest.Action` /
  `OrderCalcProfitRequest.Action` (`ENUM_ORDER_TYPE`). Existing C# code that
  assigns raw integers to these fields no longer compiles and must migrate to the
  named values (or an explicit `(EnumType)value` cast). See
  [MIGRATION.md](./MIGRATION.md).
- The change is **wire-compatible**: field numbers are preserved and proto3 enums
  encode as the same varint as the prior `int32`, so no data/wire migration is
  required and unknown/future numeric values still round-trip (open-enum
  semantics).

### Added

- Shared `ENUM_ORDER_TYPE` (all nine members) used by both trade submission and
  the margin/profit calculation requests, giving one type identity across uses.
- `TRADE_ACTION_UNSPECIFIED = 0` sentinel on `ENUM_TRADE_REQUEST_ACTIONS`
  (proto3-required zero; not an MT5 action). The server now rejects an unset or
  `TRADE_ACTION_UNSPECIFIED` trade action with a structured error and places no
  order.

### Changed

- Proto contract identity updated to `protos-003-csharp-request-enums`; tested
  server range bumped to `[0.2.0,1.0.0)`.

## 0.1.0

- Added initial `MetaTrader.Grpc.Client` package targeting `netstandard2.0`.
- Generated C# protobuf and gRPC client bindings from `protos/*.proto`.
- Added typed wrapper results for transport, gRPC status, and MT5 error payload failures.
- Documented proto contract identity `protos-001-csharp-client-library` and tested server range `[0.1.0,1.0.0)`.
