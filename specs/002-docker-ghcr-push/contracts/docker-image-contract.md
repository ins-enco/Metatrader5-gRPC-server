# Contract: Published Docker Image

**Branch**: `002-docker-ghcr-push` | **Date**: 2026-05-14

This document defines the public contract for the Docker image published to
GHCR by this feature. Consumers (operators, integration testers) rely on this
contract to pull and run the MT5 gRPC server without a local build environment.

---

## Image Reference

```
ghcr.io/ins-enco/mt5-grpc-server:<tag>
```

## Tag Catalogue

| Tag | Updated on | Stability |
|-----|-----------|-----------|
| `latest` | Every release tag push (`v*.*.*`, non-pre-release) | Stable |
| `v1.2.3` | Tag push `v1.2.3` | Immutable (overwritten only if the same tag is force-pushed) |
| `1.2` | Tag push `v1.2.*` | Mutable (updated on every `v1.2.x` release) |
| `main` | Every push to the `main` branch | Mutable (rolling) |
| `v1.2.3-rc1` | Pre-release tag push | Mutable (until final release) |

## Platform

`linux/amd64` only. The image will not run natively on ARM hosts.

## Exposed Port

| Port | Protocol | Purpose |
|------|----------|---------|
| `50051` | TCP (gRPC) | MT5 gRPC server endpoint |

## Required Runtime Environment Variables

| Variable | Required | Description |
|----------|----------|-------------|
| `MT5_SETUP_URL` | **Yes** | Download URL for the MetaTrader 5 Windows installer |
| `GRPC_HOST` | No | Bind address (default `0.0.0.0`) |
| `GRPC_PORT` | No | Listen port (default `50051`) |

## Optional Runtime Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `MT5_TERMINAL_PATH` | (set by entrypoint) | Path to MT5 executable inside Wine prefix |
| `MT5_INSTALL_TIMEOUT` | (set by entrypoint) | Seconds to wait for MT5 installation |
| `MT5_STARTUP_DELAY` | (set by entrypoint) | Seconds to wait for MT5 to start |
| `NUMPY_SPEC` | `numpy<2` | pip version specifier for numpy |
| `GRPC_VERBOSE` | `false` | Enable verbose gRPC logging |

## Persistent State (Volume)

The image expects a writable volume mounted at `/wineprefix` for Wine prefix
state, MT5 installation, and pip package cache. Without this volume, every
container restart re-downloads and re-installs all components.

```yaml
volumes:
  - wineprefix:/wineprefix
```

## OCI Labels on Every Published Image

| Label | Value |
|-------|-------|
| `org.opencontainers.image.revision` | Full git commit SHA (traceability) |
| `org.opencontainers.image.source` | GitHub repository URL |
| `org.opencontainers.image.created` | ISO-8601 build timestamp |

## Traceability

Every image can be traced to its exact source commit via:

```bash
docker inspect ghcr.io/ins-enco/mt5-grpc-server:<tag> \
  --format '{{ index .Config.Labels "org.opencontainers.image.revision" }}'
```

## Out-of-Scope Guarantees

- No vulnerability scan results are included or attested.
- No SBOM attestation is attached.
- No cryptographic signature is applied.
- No multi-architecture support.

These are deferred to a future hardening phase per spec clarifications.
