# Phase 1 Data Model: Prebuilt Self-Contained Image with Per-Login Containers

**Feature**: `006-prebuilt-image-per-login`
**Date**: 2026-07-08

This feature introduces no wire-format or persisted data entities. The "data" it
manages is deployment configuration and container topology. The entities below
are the operational/config entities from the spec, with their fields, validation
rules, and lifecycle.

## Entity: Prebuilt image (build artifact)

An immutable, self-contained image that already includes the Python runtime,
VC++ runtime, MT5 terminal, and gRPC server in a baked Wine prefix.

| Field | Type | Source | Notes / Validation |
| --- | --- | --- | --- |
| `PYTHON_VERSION` | build ARG (string) | Dockerfile | Pinned; default `3.11.9`. Must match a valid python.org release. |
| `NUMPY_SPEC` | build ARG (string) | Dockerfile | Default `numpy<2` (Wine/UCRT compatibility). |
| `MT5_SETUP_URL` | build ARG (URL) | Dockerfile | Default MetaQuotes CDN; overridable to a pinned mirror. Build FAILS if unreachable. |
| baked `/wineprefix` | image directory | build layers | Plain directory (no `VOLUME`). Contains Python, VC++ runtime, MT5, packages. |
| terminal binary present | build invariant | build check | `/wineprefix/drive_c/Program Files/MetaTrader 5/terminal64.exe` MUST exist at end of build or the build fails. |
| image name | registry ref | CI | `ghcr.io/<owner>/mt5-grpc-server-prebuilt`. Private package. |

**Lifecycle**: built (CI or local) → pushed to GHCR (private) → pulled → run as
one or more per-login containers. Immutable; rebuild from pinned inputs yields a
functionally equivalent image (SC-006).

## Entity: Per-login container instance

A running container bound to exactly one MT5 account, with its own port and its
own writable layer.

| Field | Type | Source | Notes / Validation |
| --- | --- | --- | --- |
| `MT5_LOGIN` | env (string) | launcher `--login` | Required. Identifies the account and the container name. |
| `MT5_PASSWORD` | env (string, secret) | launcher `--password` | Optional at launch but required for auto-login; redacted in logs. |
| `MT5_SERVER` | env (string) | launcher `--server` | Broker server name for auto-login. |
| host port | int (1–65535) | launcher `--port` | Required. Published as `127.0.0.1:<port>:50051`. MUST be unique per host; collision fails clearly. |
| container name | string | derived / `--name` | Default `mt5-grpc-<login>`. MUST be unique; launcher refuses if it already exists. |
| `GRPC_HOST` | env | default `0.0.0.0` | Bind inside container (kept `0.0.0.0` for publishing). |
| `GRPC_PORT` | env | default `50051` | In-container listen port. |
| `GRPC_VERBOSE` | env | default `true` | Logging + redaction preserved from existing image. |
| writable layer | container CoW layer | Docker | Holds terminal profile/data, prefix mutations, logs. Ephemeral; reset on recreation. |
| binding scope | host publish | launcher | Defaults to `127.0.0.1` (FR-013); operator must opt in to `0.0.0.0`. |

**State transitions**:
`created → running (Xvfb → MT5 launched → gRPC serving) → stopped/removed`.
On recreation, the writable layer is fresh: the container re-establishes its MT5
session from configuration (env). No persisted state carries over.

**Isolation invariant**: stopping/recreating/failing one instance has zero
effect on other instances (SC-004) because they share no volume and each owns
its writable layer and its host port.

## Entity: Deployment option

The operator's choice of deployment style.

| Option | Image | State model | When to use |
| --- | --- | --- | --- |
| Bootstrap (existing) | `mt5-grpc-server` | shared persistent `wineprefix` volume; installs on first start | Small image, single/shared account, persistent prefix acceptable |
| Prebuilt (new) | `mt5-grpc-server-prebuilt` | zero volumes; per-container writable layer; no runtime install | Fast/predictable start, immutable, multi-tenant one-container-per-login |

**Rule**: the two options are additive and mutually independent; selecting the
prebuilt option MUST NOT change the bootstrap option's behavior (FR-008).

## Relationships

- One **Prebuilt image** → many **Per-login container instances** (1:N on a host).
- One **Per-login container instance** → exactly one **MT5 login** (1:1).
- **Deployment option** selects which image an operator runs; both map to the
  same gRPC contract and server behavior.
