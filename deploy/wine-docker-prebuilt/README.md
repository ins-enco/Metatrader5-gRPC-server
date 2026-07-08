# Prebuilt self-contained MT5 gRPC image

This is the **prebuilt** deployment option. It bakes the Windows Python runtime,
the Microsoft VC++ runtime, the MetaTrader 5 terminal, and the gRPC server
packages into a populated `/wineprefix` at **build time**. Containers therefore:

- start serving gRPC in **under 60s** with **no runtime install/download**,
- mount **no volume** — each container keeps its state in its own ephemeral
  copy-on-write writable layer, and
- run **one isolated container per MT5 login** on a single host.

It is **additive**: the bootstrap option in [`deploy/wine-docker/`](../wine-docker/)
is unchanged. Pick whichever fits your needs.

## Prebuilt vs. bootstrap trade-offs

| | Bootstrap (`deploy/wine-docker/`) | Prebuilt (this directory) |
| --- | --- | --- |
| Image | `mt5-grpc-server` | `mt5-grpc-server-prebuilt` |
| First start | installs Python/VC++/MT5 on first run | nothing installs at runtime |
| Time to serve | minutes (first run) | < 60s |
| State model | shared persistent `wineprefix` **volume** | **zero volumes**; per-container writable layer |
| Image size | small | large (bundles MT5 + runtimes) |
| Best for | single/shared account, persistent prefix | fast/immutable start, one container per login |

## Build or pull

Build locally (pins are overridable build ARGs):

```bash
docker build \
  -f deploy/wine-docker-prebuilt/Dockerfile \
  --build-arg PYTHON_VERSION=3.11.9 \
  --build-arg NUMPY_SPEC='numpy<2' \
  --build-arg MT5_SETUP_URL='https://download.mql5.com/cdn/web/metaquotes.software.corp/mt5/mt5setup.exe' \
  -t ghcr.io/ins-enco/mt5-grpc-server-prebuilt:latest \
  .
```

The build **fails** (non-zero, no image produced) if the MT5 terminal binary is
absent afterwards, so an incomplete image is never published.

Or pull the published **private** image (after `docker login ghcr.io`):

```bash
docker pull ghcr.io/ins-enco/mt5-grpc-server-prebuilt:latest
```

## Run one container

```bash
docker run -d --name mt5-grpc-100200300 \
  --restart unless-stopped --shm-size 1gb \
  -p 127.0.0.1:50051:50051 \
  -e MT5_LOGIN=100200300 -e MT5_PASSWORD=secret -e MT5_SERVER=Broker-Demo \
  ghcr.io/ins-enco/mt5-grpc-server-prebuilt:latest
```

The included [`docker-compose.yml`](./docker-compose.yml) is a single-container
example with **no `volumes:` section**. Confirm the server is up within ~60s and
that the logs contain no install/download lines:

```bash
docker logs -f mt5-grpc-100200300   # no Python/VC++/pip/MT5 install lines
```

## Run one container per login (launcher)

Use the launcher to bring up dozens of isolated per-login containers, each with
its own login, host port, and writable layer:

```bash
# Linux/macOS
deploy/wine-docker-prebuilt/run-login.sh --login 100200300 --port 50051 \
  --password secretA --server Broker-Demo

deploy/wine-docker-prebuilt/run-login.sh --login 100200301 --port 50052 \
  --password secretB --server Broker-Demo
```

```powershell
# Windows hosts (same flag surface)
deploy\wine-docker-prebuilt\run-login.ps1 -Login 100200300 -Port 50051 `
  -Password secretA -Server Broker-Demo
```

Each container is named `mt5-grpc-<login>`, publishes `127.0.0.1:<port>:50051`,
and is fully isolated. Removing one (`docker rm -f mt5-grpc-100200300`) does not
affect the others. Re-running with a host port already in use **fails clearly**
rather than hijacking the existing endpoint.

Run `run-login.sh --help` for the full flag surface. Exit codes: `0` started,
`2` bad args, `3` container name exists, non-zero for Docker errors (e.g. port
in use, surfaced verbatim).

## Runtime environment variables

The prebuilt image honors exactly the runtime configuration surface documented
in [`contracts/container-env.md`](../../specs/006-prebuilt-image-per-login/contracts/container-env.md):
`MT5_LOGIN`, `MT5_PASSWORD`, `MT5_SERVER`, `GRPC_HOST`, `GRPC_PORT`,
`GRPC_VERBOSE` (default `true`), `MT5_STARTUP_DELAY`, `DISPLAY`, `XVFB_SCREEN`,
`WINEPREFIX`. The install/download variables (`MT5_SETUP_URL`,
`MT5_INSTALL_TIMEOUT`, `NUMPY_SPEC`) are **build ARGs** and have **no runtime
effect**. Verbose request/response logging with secret redaction (`password`,
`token`, `secret`, `api_key`, …) is identical to the bootstrap image.

## Security: keep both GHCR packages private

Both GHCR packages — `mt5-grpc-server` (bootstrap) and `mt5-grpc-server-prebuilt`
(this image) — **MUST be set private** (authentication required to pull). The
prebuilt package in particular **MUST stay private** because it **redistributes
the MetaTrader 5 terminal**; a public prebuilt package is not permitted.

Endpoints default to `127.0.0.1`. Only expose them (`--bind 0.0.0.0`) behind TLS
or a firewall.

## Reproducibility

Rebuilding from the same `PYTHON_VERSION`, `NUMPY_SPEC`, and a pinned
`MT5_SETUP_URL` yields a functionally equivalent image. The default MetaQuotes
CDN URL is **not** version-pinned, so for strict byte/version reproducibility,
override `MT5_SETUP_URL` with a **pinned internal mirror** of the installer:

```bash
docker build -f deploy/wine-docker-prebuilt/Dockerfile \
  --build-arg MT5_SETUP_URL='https://mirror.internal.example/mt5/mt5setup-<pinned>.exe' \
  -t ghcr.io/ins-enco/mt5-grpc-server-prebuilt:latest .
```

## Known behaviors

- **Ephemeral state**: recreating a container resets its writable layer
  (terminal cache, logs); it re-establishes the session from env on next start.
- **Baked MT5 may self-update**: any update the terminal applies writes only to
  the writable layer and is lost on recreation.
- **Host resources**: many terminals + headless displays on one host can exhaust
  memory/CPU/shm. Size the host and `--shm-size` (default `1gb`) accordingly.

## Tests

Deployment/topology tests (no live broker required) live in [`tests/`](./tests/):

| Test | Verifies |
| --- | --- |
| `test_build.sh` | build succeeds; `terminal64.exe` baked (SC-006) |
| `test_single_container.sh` | ready < 60s, no install lines, zero mounts (SC-001/003) |
| `test_offline_start.sh` | starts with installer sources unreachable (US1 scenario 3) |
| `test_launcher_args.sh` | argument validation + exit codes (2/3/0) |
| `test_port_collision.sh` | in-use host port fails clearly (edge case) |
| `test_multi_login.sh` | two logins isolated; one-down-others-up (SC-002/004) |
| `test_quickstart_walkthrough.md` | docs-alone walkthrough of SC-001..SC-006 |
