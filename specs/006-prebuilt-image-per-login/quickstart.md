# Quickstart: Prebuilt Self-Contained Image with Per-Login Containers

**Feature**: `006-prebuilt-image-per-login`

Two deployment options are available. Pick one:

| Option | Image | State | Best for |
| --- | --- | --- | --- |
| **Bootstrap** (existing) | `mt5-grpc-server` | shared persistent `wineprefix` volume; installs on first start | small image, single/shared account, persistent prefix |
| **Prebuilt** (new) | `mt5-grpc-server-prebuilt` | zero volumes; per-container writable layer; no runtime install | fast/immutable start, one isolated container per login |

Both publish to GHCR as **private** packages (authenticate to pull).

## A. Build or pull the prebuilt image

Build locally (pins are overridable ARGs):

```bash
docker build \
  -f deploy/wine-docker-prebuilt/Dockerfile \
  --build-arg PYTHON_VERSION=3.11.9 \
  --build-arg NUMPY_SPEC='numpy<2' \
  --build-arg MT5_SETUP_URL='https://download.mql5.com/cdn/web/metaquotes.software.corp/mt5/mt5setup.exe' \
  -t ghcr.io/<owner>/mt5-grpc-server-prebuilt:latest \
  .
```

Or pull the published private image (after `docker login ghcr.io`):

```bash
docker pull ghcr.io/<owner>/mt5-grpc-server-prebuilt:latest
```

> For strict reproducibility, point `MT5_SETUP_URL` at a pinned internal mirror.

## B. Run one container (verifies US1)

```bash
docker run -d --name mt5-grpc-100200300 \
  --restart unless-stopped --shm-size 1gb \
  -p 127.0.0.1:50051:50051 \
  -e MT5_LOGIN=100200300 -e MT5_PASSWORD=secret -e MT5_SERVER=Broker-Demo \
  ghcr.io/<owner>/mt5-grpc-server-prebuilt:latest
```

Confirm it serves within ~60s **without** any install/download step in the logs:

```bash
docker logs -f mt5-grpc-100200300   # no Python/VC++/pip/MT5 install lines
```

## C. Run one container per login with the launcher (verifies US2)

```bash
deploy/wine-docker-prebuilt/run-login.sh --login 100200300 --port 50051 \
  --password secretA --server Broker-Demo

deploy/wine-docker-prebuilt/run-login.sh --login 100200301 --port 50052 \
  --password secretB --server Broker-Demo
```

Each container is `mt5-grpc-<login>`, publishes its own `127.0.0.1:<port>`, and
keeps state in its own writable layer. Connect a client to each port and confirm
each returns its own account's data. Stop one:

```bash
docker rm -f mt5-grpc-100200300     # the other keeps serving (SC-004)
```

Port-collision check (edge case): re-running with a port already in use fails
clearly instead of hijacking the other endpoint.

## D. Choose between options (US3)

- Use **bootstrap** when you want a small image and a persistent shared prefix.
- Use **prebuilt** when you want fast, immutable, multi-tenant per-login
  containers with no volumes.
- The bootstrap option is unchanged; adding prebuilt does not affect it (FR-008).

## Verification checklist (maps to Success Criteria)

- [ ] SC-001: container ready < 60s, no runtime install lines in logs.
- [ ] SC-002: dozens of per-login containers via the launcher, each isolated.
- [ ] SC-003: `docker inspect` shows zero volume mounts.
- [ ] SC-004: removing one container does not disrupt others.
- [ ] SC-005: a new operator can follow this doc to launch either option and
      multiple per-login containers in < 15 min.
- [ ] SC-006: rebuild from the same pinned inputs yields an equivalent image.

## Notes / known behaviors

- **Ephemeral state**: recreating a container resets its writable layer
  (terminal cache, logs); it re-establishes the session from env on next start.
- **Baked MT5 may self-update**: any update writes to the writable layer only
  and is lost on recreation.
- **Security**: endpoints default to `127.0.0.1`. Expose (`--bind 0.0.0.0`) only
  behind TLS or a firewall.
- **Host resources**: many terminals + headless displays on one host can
  exhaust memory/CPU/shm; size the host and `--shm-size` accordingly.
