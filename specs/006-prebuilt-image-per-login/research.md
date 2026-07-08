# Phase 0 Research: Prebuilt Self-Contained Image with Per-Login Containers

**Feature**: `006-prebuilt-image-per-login`
**Date**: 2026-07-08

This feature is packaging/deployment-only. No `.proto`, RPC, message, server
adapter, or client behavior changes. Research focuses on how to move the
existing first-run bootstrap work into build time and how to run one isolated
container per MT5 login.

## Decision 1: Bake the Wine prefix at build time (no runtime install, no volume)

- **Decision**: Move every step the existing `entrypoint.sh` performs on first
  start — `wineboot`, `winetricks vcrun2022`, Windows Python install, pip
  install of `mt5_grpc_proto` + `mt5_grpc_server` (+ `numpy<2`), and the MT5
  terminal install — into `RUN` layers of a new Dockerfile so the populated
  `/wineprefix` ships inside the image. The prebuilt entrypoint performs **no**
  install/download; it only starts Xvfb, writes the per-login autostart INI,
  launches MT5, and starts the gRPC server.
- **Rationale**: FR-002 forbids runtime installation; SC-001 requires readiness
  under 60s; FR-003 forbids any mounted volume. Baking the prefix into image
  layers satisfies all three. Because the prefix is in the read-only image, each
  container's changes go to its own copy-on-write writable layer (FR-004),
  giving per-login isolation for free.
- **Alternatives considered**:
  - *Keep runtime install but pre-cache installers* — rejected: still runs
    install steps at start (violates FR-002) and is slow (violates SC-001).
  - *Shared named volume seeded once* — rejected: violates FR-003 and
    reintroduces the cross-container contention this feature exists to remove.

## Decision 2: `WINEPREFIX=/wineprefix` as a plain image directory (not a mount)

- **Decision**: Build the prefix at a fixed in-image path (`/wineprefix`) and do
  **not** declare a `VOLUME` for it. The prebuilt compose file mounts nothing.
- **Rationale**: Assumption "writable-layer state is ephemeral by design" and
  FR-003/SC-003 (zero volume mounts). A `VOLUME` instruction would create an
  anonymous volume per container and muddy the "no volumes" contract, so it is
  intentionally omitted.
- **Alternatives considered**: `VOLUME /wineprefix` — rejected; it would spawn
  anonymous volumes and contradict the zero-volume success criterion.

## Decision 3: Reproducible build inputs (pinned Python, defined MT5 source)

- **Decision**: Pin `PYTHON_VERSION` (default `3.11.9`, matching the existing
  image) and `NUMPY_SPEC` (`numpy<2`) as build `ARG`s, and take the MT5 installer
  from a build `ARG MT5_SETUP_URL` defaulting to the current MetaQuotes CDN URL,
  overridable to an internal pinned mirror. Base image pinned to `ubuntu:24.04`
  (same as existing). The build fails (non-zero) if the MT5 terminal binary is
  absent after install, so no incomplete image is produced.
- **Rationale**: FR-009, FR-010, SC-006, and the "build-time source
  unavailable" edge case. `wget` already fails the build on an unreachable URL;
  an explicit post-install check on `terminal64.exe` covers a silently
  incomplete install.
- **Alternatives considered**:
  - *Fully content-addressed MT5 pin (checksum)* — deferred: the MetaQuotes CDN
    URL is not versioned/hashed publicly; documenting an overridable mirror ARG
    is the reproducibility lever available in v1. Noted in quickstart as an
    operator responsibility for strict reproducibility.

## Decision 4: MT5 terminal self-update handled headlessly at build time

- **Decision**: Run the MT5 installer under Wine + Xvfb during the build exactly
  as the runtime entrypoint does today (`wine mt5setup.exe /auto`, wait for
  `terminal64.exe`). Any runtime self-update the baked terminal attempts writes
  only to the container's writable layer and is lost on recreation.
- **Rationale**: MT5 is Windows-native and needs a display even for silent
  install; the existing approach already works under Xvfb. Matches the "baked
  MT5 becomes outdated" edge case: updates are ephemeral, documented, not
  persisted.
- **Alternatives considered**: Disabling MT5 auto-update via config — out of
  scope for v1; documented as a known behavior instead.

## Decision 5: Parameterized per-login launcher

- **Decision**: Ship a small launcher script (`run-login.sh`, POSIX/bash;
  optional `run-login.ps1` for Windows hosts) that takes `--login` and `--port`
  (plus optional `--password`, `--server`, `--name`) and runs `docker run -d`
  with a per-login container name (`mt5-grpc-<login>`), a
  `127.0.0.1:<port>:50051` publish, `--shm-size 1gb`, `--restart unless-stopped`,
  and the per-login env vars. It refuses to start if the container name already
  exists and surfaces Docker's port-in-use error rather than masking it.
- **Rationale**: FR-016 (parameterized launcher, scales to dozens), FR-006/FR-007
  (per-login creds + distinct port), FR-013 (default `127.0.0.1` binding), and
  the "host port collision" edge case. A thin wrapper over `docker run` keeps the
  zero-volume, writable-layer-only model intact and needs no orchestration
  dependency.
- **Alternatives considered**:
  - *docker compose with a per-login `.env`* — offered as a documented
    alternative for a handful of logins, but hand-editing per account does not
    satisfy FR-016's "without hand-editing configuration for each account".
  - *Kubernetes/Swarm* — out of scope for v1 (single host, dozens of
    containers); noted as a future direction.

## Decision 6: Two distinct GHCR release artifacts, both private

- **Decision**: Extend `.github/workflows/docker-ghcr.yml` to build and push two
  images from two Dockerfiles: the unchanged bootstrap image
  (`ghcr.io/<owner>/mt5-grpc-server`) and the new prebuilt image
  (`ghcr.io/<owner>/mt5-grpc-server-prebuilt`), each with its own metadata/tags.
  Both packages are set to **private** in GHCR (authentication required to pull).
- **Rationale**: FR-014 (two identifiable artifacts), FR-015 (both private; the
  prebuilt one redistributes MT5 so private is mandatory), FR-008 (bootstrap
  unchanged). Distinct image names keep the two artifacts unambiguous.
- **Alternatives considered**:
  - *Single image name, two tags* — rejected: harder to keep the "prebuilt
    redistributes MT5, keep private" rule and confuses the two trade-off
    profiles.
  - *Public prebuilt package* — rejected outright by FR-015 (MT5 redistribution).

## Decision 7: Preserve logging + redaction behavior unchanged

- **Decision**: The prebuilt entrypoint invokes the same
  `mt5_grpc_server.grpc_server` module with the same `--host/--port/--verbose`
  surface and the same `GRPC_VERBOSE` default, so request/response logging and
  secret redaction (`password`, `token`, `secret`, `api_key`, …) are identical.
- **Rationale**: FR-011 and the constitution's observability principle. No server
  code changes; only the provisioning path differs.

## Open items

None. All spec clarifications (release types, GHCR visibility, ephemeral state,
launcher scale) were resolved in the 2026-07-08 clarification session.
