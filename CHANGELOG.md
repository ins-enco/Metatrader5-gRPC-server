# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.3.0] - 2026-07-07

### Added
- **Trade transaction event streaming** — new `TradeEventsService.SubscribeTradeTransactions`,
  the first server-streaming RPC in the contract. Emits one `TradeTransactionEvent`
  per newly added deal on the connected account, exactly once, in chronological
  order, with no duplicates (de-duplicated on the deal ticket so same-millisecond
  deals are both delivered). Backed by server-side polling of `history_deals_get`
  over an advancing `(time_msc, ticket)` watermark (no push callback exists in the
  MT5 Python API).
- Subscription starts at "now" by default (no historical replay); an explicit past
  start backfills once, capped to a 7-day lookback. Poll cadence defaults to 1000 ms,
  clamped to a server floor of 200 ms. Failures surface as an in-band `Error` frame.
- Server worker pool raised from 10 to 32 to budget for concurrent long-lived
  streams without starving unary calls.

### Changed
- Coordinated backward-compatible minor bump of `mt5_grpc_proto`, `mt5_grpc_server`,
  and `MetaTrader.Grpc.Client` to `0.3.0`. No existing RPC, message, field, or field
  number changed — fully additive.

## [0.2.0] - 2025-03-09

### Added
- Verbose logging functionality with a new `--verbose` command-line option
- Logging interceptor for detailed request and response logging
- Bump version script for easier version management

### Changed
- Improved README with additional use case diagrams
- Removed excessive MT5 initialization and shutdown code for better performance
- Updated package version to 0.1.1 in setup files

## [0.1.0] - 2024-12-25

### Added
- Initial release of MT5 gRPC Server
- Basic MetaTrader 5 operations support through gRPC
- Protocol buffer definitions for MT5 operations
- Example client implementation
- Basic documentation
- Windows support under Wine for Linux/macOS users

### Dependencies
- Python >=3.8
- gRPC framework
- MetaTrader 5 terminal
- Required Python packages listed in requirements.txt 