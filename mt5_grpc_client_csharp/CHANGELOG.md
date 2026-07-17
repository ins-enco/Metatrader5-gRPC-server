# Changelog

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
