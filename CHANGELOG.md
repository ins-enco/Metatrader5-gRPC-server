# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.4.0] - 2026-09-07

### Added
- **Chunked deal-history streaming** — new `TradeHistoryService.StreamDeals`
  (`DealsRequest` → `stream DealsResponse`), which applies exactly the same filters
  as `GetDeals` but delivers the result as a sequence of bounded messages instead
  of one. `GetDeals` on a large history builds a single oversized response — 1571
  deals measured at ~121 KB — which aborts grpcio's Windows IOCP endpoint under
  Wine (`windows_endpoint.cc`, exit `c0000409`) and puts a `restart: unless-stopped`
  container into a restart loop. Streaming removes that failure mode; 982 deals
  (~75 KB) were never observed to fail.
- New `DealsRequest.chunk_size` (**field 5**, `optional uint32`): deals per streamed
  message. Unset defaults to 500, values above the cap are clamped to 1000, and a
  transmitted `0` is treated as unset. `StreamDeals` only — `GetDeals` ignores it.
  A chunk at the 1000 cap is ~77 KB and so re-enters the uncertain band, which is
  why the default is 500.
- `StreamDeals` stops converting deals as soon as the caller goes away
  (`context.is_active()`), and reports a terminal failure as a single response
  carrying only `error`. An empty filtered history completes the stream without
  yielding a message.
- `mt5_grpc_server/tests/test_deals_history_stream.py` — mock-MT5 coverage for
  chunk boundaries, chunk-size clamping, an empty history, error-only output and
  early client exit. No live broker required.

### Changed
- Coordinated backward-compatible minor bump of `mt5_grpc_proto` and
  `mt5_grpc_server` to `0.4.0`. **The change is purely additive**: no existing RPC,
  message, field or field number was changed, renamed, reused or removed, and
  `GetDeals`, `DealsResponse` and `Deal` are untouched. `0.4.0` is the first
  version whose contract contains `StreamDeals`, so it is the checkable floor for
  clients that need it.
- `MetaTrader.Grpc.Client` `5.1.0` exposes the RPC as `StreamDealsAsync` plus a
  whole-history convenience wrapper `GetAllDealsAsync`, and narrows its tested
  server range to `[0.4.0,1.0.0)`. Neither operation falls back to the other: on a
  pre-`0.4.0` server the call fails as `Unimplemented` rather than silently
  reverting to the response size that crashes the server.

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