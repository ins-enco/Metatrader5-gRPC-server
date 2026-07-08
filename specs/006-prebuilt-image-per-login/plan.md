# Implementation Plan: Prebuilt Self-Contained Image with Per-Login Containers

**Branch**: `006-prebuilt-image-per-login` | **Date**: 2026-07-08 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/006-prebuilt-image-per-login/spec.md`

## Summary

Add a second, additive Docker deployment option: a **prebuilt self-contained
image** that bakes the Windows Python runtime, the Microsoft VC++ runtime, the
MetaTrader 5 terminal, and the gRPC server into the image at build time (inside a
populated `/wineprefix`), so containers start serving gRPC in under 60s with no
runtime install/download and no mounted volume. Each container keeps its state in
its own copy-on-write writable layer, enabling **one isolated container per MT5
login** on a single host. A parameterized launcher (`run-login.sh`, taking a
login and a port) brings up dozens of per-login containers. Both the existing
bootstrap image and the new prebuilt image are published to GHCR as **private**
packages. This is a packaging/deployment-only change: no `.proto`, RPC, server
adapter, or client behavior is modified.

Technical approach: create `deploy/wine-docker-prebuilt/` (Dockerfile,
entrypoint, launcher, compose, README) by lifting the existing
`entrypoint.sh` first-run steps into build-time `RUN` layers; extend
`.github/workflows/docker-ghcr.yml` to build/push both images; document both
options.

## Technical Context

**Language/Version**: Bash (entrypoint + launcher), Dockerfile; Windows Python
`3.11.9` (baked, unchanged) runs the existing server. Optional PowerShell
launcher variant for Windows hosts.
**Primary Dependencies**: Docker/BuildKit, Ubuntu 24.04 base, Wine + Xvfb +
winetricks (`vcrun2022`), MetaTrader 5 terminal, existing `mt5_grpc_proto` and
`mt5_grpc_server` packages. No new runtime dependency added to the server.
**Storage**: None. No volumes (FR-003). Per-container writable layer only,
ephemeral by design.
**Testing**: Docker build of the prebuilt image; container smoke test (start →
gRPC ready without install lines); multi-container isolation test (two logins,
two ports); launcher argument/collision tests; existing server unit/contract
tests remain green (unchanged). No live broker required for build/topology
checks.
**Target Platform**: Linux host running Docker; Windows runtime emulated via
Wine (single host, dozens of containers).
**Project Type**: Deployment/packaging for a gRPC server (single-repo,
Docker-based).
**Performance Goals**: Container ready to accept gRPC in < 60s (SC-001); ~dozens
of concurrent per-login containers on one host (SC-002).
**Constraints**: Zero volume mounts (FR-003/SC-003); no runtime install
(FR-002); reproducible from pinned build inputs (FR-010/SC-006); both GHCR
packages private (FR-015); default host-local binding (FR-013); logging +
redaction preserved (FR-011).
**Scale/Scope**: One host, ~dozens of per-login containers; two GHCR release
artifacts; one new deploy directory + one CI workflow edit + docs. No source
code in `mt5_grpc_server` / `mt5_grpc_proto` changes.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

- **Protocol contract**: **PASS.** No `.proto`, RPC, message, field, or field
  number is added, removed, or modified. Spec "Protocol and MT5 Contract Impact"
  = None / backward-compatible; [contracts/README.md](./contracts/README.md)
  confirms zero wire-format impact. No bindings to regenerate.
- **MT5 behavior fidelity**: **PASS.** MT5 operation mapping is unchanged
  (spec). The server talks to the terminal exactly as today; only the terminal's
  provisioning moves from first-run install to build-time bundling. Per-login
  containers each run one terminal bound to one account
  ([data-model.md](./data-model.md)).
- **Multi-language type safety**: **PASS.** No public contract surface changes;
  no new scalar encodings, optionals, or sentinels. Clients in any language are
  unaffected (spec Cross-Language Type Notes = None).
- **Reliability and observability**: **PASS.** Verbose request/response logging
  and secret redaction (`password`, `token`, `secret`, `api_key`, …) preserved
  unchanged (FR-011, [contracts/container-env.md](./contracts/container-env.md)).
  Failure modes covered: build fails on missing MT5/unreachable source; launcher
  fails clearly on port collision / name clash; per-login failure isolation
  (SC-004); host resource pressure documented; ephemeral-state behavior
  documented.
- **Test and release reproducibility**: **PASS.** Build is reproducible from
  pinned `PYTHON_VERSION` / `NUMPY_SPEC` / defined `MT5_SETUP_URL`
  ([contracts/build-args.md](./contracts/build-args.md), SC-006). Release path is
  scripted via `.github/workflows/docker-ghcr.yml` (two images). Verification
  commands are listed in [quickstart.md](./quickstart.md). Existing generation/
  unit/contract checks are unaffected (no code change) and still runnable.

**Result**: All gates PASS. No entries required in Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/006-prebuilt-image-per-login/
├── plan.md              # This file (/speckit-plan command output)
├── spec.md              # Feature specification (input)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── README.md        # No proto change; index of operator-facing contracts
│   ├── launcher-cli.md  # Per-login launcher command surface
│   ├── container-env.md # Runtime environment variable contract
│   └── build-args.md    # Prebuilt image build-argument contract
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
deploy/
├── wine-docker/                 # EXISTING bootstrap option — unchanged (FR-008)
│   ├── Dockerfile
│   ├── entrypoint.sh
│   ├── docker-compose.yml
│   └── README.md
└── wine-docker-prebuilt/        # NEW prebuilt self-contained option
    ├── Dockerfile               # Bakes Python + VC++ + MT5 + server into /wineprefix
    ├── entrypoint.sh            # Runtime-only: Xvfb → autostart INI → MT5 → gRPC (no install)
    ├── run-login.sh             # Parameterized per-login launcher (FR-016) — bash
    ├── run-login.ps1            # Optional PowerShell launcher variant (Windows hosts)
    ├── docker-compose.yml       # Single-container example, zero volumes
    └── README.md                # Prebuilt option docs + trade-offs + multi-login guide

.github/workflows/
└── docker-ghcr.yml              # EDIT: build + push BOTH images (bootstrap + prebuilt), both private

README.md                        # EDIT: point to both deployment options
```

**Structure Decision**: Keep the existing `deploy/wine-docker/` untouched and add
a sibling `deploy/wine-docker-prebuilt/` for the additive option (FR-008). The
prebuilt Dockerfile lifts the current runtime install steps
([deploy/wine-docker/entrypoint.sh](../../deploy/wine-docker/entrypoint.sh) lines
49–87) into build-time `RUN` layers so `/wineprefix` ships inside the image; the
prebuilt entrypoint retains only the runtime portion (Xvfb, autostart INI, MT5
launch, gRPC server — lines 4–19 and 97–134). The CI workflow gains a second
build/push target. No changes under `mt5_grpc_server/`, `mt5_grpc_proto/`, or
`protos/`.

## Complexity Tracking

> No Constitution Check violations. This section intentionally left empty.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| (none) | — | — |
