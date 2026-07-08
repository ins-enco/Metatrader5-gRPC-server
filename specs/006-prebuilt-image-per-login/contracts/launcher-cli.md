# Contract: Per-login launcher CLI

**File**: `deploy/wine-docker-prebuilt/run-login.sh` (bash) and optional
`run-login.ps1` (PowerShell, same surface).

Satisfies FR-016, FR-006, FR-007, FR-013, and the "host port collision" edge case.

## Synopsis

```text
run-login.sh --login <LOGIN> --port <HOST_PORT> [options]
```

## Arguments

| Flag | Required | Default | Meaning |
| --- | --- | --- | --- |
| `--login <LOGIN>` | yes | — | MT5 account login. Sets `MT5_LOGIN` and, unless `--name` is given, the container name `mt5-grpc-<LOGIN>`. |
| `--port <HOST_PORT>` | yes | — | Host port to publish. Integer 1–65535. Published as `<BIND>:<HOST_PORT>:50051`. |
| `--password <PW>` | no | unset | MT5 password (`MT5_PASSWORD`). Redacted in logs. |
| `--server <SRV>` | no | unset | MT5 broker server (`MT5_SERVER`). |
| `--name <NAME>` | no | `mt5-grpc-<LOGIN>` | Explicit container name. |
| `--image <REF>` | no | `ghcr.io/<owner>/mt5-grpc-server-prebuilt:latest` | Prebuilt image reference. |
| `--bind <ADDR>` | no | `127.0.0.1` | Host bind address. Operator must pass `0.0.0.0` explicitly to expose (FR-013). |
| `--verbose <bool>` | no | `true` | Sets `GRPC_VERBOSE`. |
| `--shm-size <SIZE>` | no | `1gb` | Passed to `docker run --shm-size`. |
| `-h`, `--help` | no | — | Usage. |

## Behavior contract

1. **Validation**: both `--login` and `--port` MUST be present; otherwise exit
   non-zero with a usage message. Port MUST be numeric and in range.
2. **Name uniqueness**: if a container named `<NAME>` already exists, the
   launcher MUST refuse (exit non-zero) rather than clobber it.
3. **Port collision**: the launcher MUST NOT mask a host-port-in-use error; it
   surfaces Docker's failure and exits non-zero (edge case: "host port
   collision" fails clearly, never silently takes over another login's
   endpoint).
4. **No volumes**: the generated `docker run` MUST NOT mount any volume
   (FR-003). State stays in the container writable layer.
5. **Isolation**: each invocation produces an independent container with its own
   name, port, and writable layer (FR-004, FR-005, SC-004).
6. **Effective command** (illustrative):
   ```text
   docker run -d --name mt5-grpc-<LOGIN> \
     --restart unless-stopped --shm-size 1gb \
     -p 127.0.0.1:<HOST_PORT>:50051 \
     -e MT5_LOGIN=<LOGIN> -e MT5_PASSWORD=*** -e MT5_SERVER=<SRV> \
     -e GRPC_HOST=0.0.0.0 -e GRPC_PORT=50051 -e GRPC_VERBOSE=true \
     ghcr.io/<owner>/mt5-grpc-server-prebuilt:latest
   ```
7. **Scale**: repeated invocations with distinct `--login`/`--port` bring up
   dozens of concurrent containers on one host (FR-016, SC-002), subject only to
   host resources.

## Exit codes

| Code | Condition |
| --- | --- |
| `0` | Container started. |
| `2` | Missing/invalid arguments. |
| `3` | Container name already exists. |
| non-zero (Docker's) | Port in use or other Docker failure, surfaced verbatim. |
