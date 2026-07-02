# Changelog

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
