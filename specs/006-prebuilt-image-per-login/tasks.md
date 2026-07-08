---
description: "Task list for prebuilt-image-per-login feature implementation"
---

# Tasks: Prebuilt Self-Contained Image with Per-Login Containers

**Input**: Design documents from `/specs/006-prebuilt-image-per-login/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: This is a packaging/deployment change (new Docker image + launcher +
CI). Per the template rule, packaging changes include tests. Tests here are
Docker build/smoke/topology and launcher argument/collision checks (no live
broker required). No `.proto`, RPC, server adapter, or client code changes, so
no contract/binding regeneration tests apply (see contracts/README.md).

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

Deployment/packaging feature. New artifacts live under
`deploy/wine-docker-prebuilt/`; deployment tests under
`deploy/wine-docker-prebuilt/tests/`; CI under `.github/workflows/`. No changes
to `mt5_grpc_server/`, `mt5_grpc_proto/`, or `protos/` (FR-008, Constitution
Check PASS).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the new deployment directory alongside the untouched bootstrap option.

- [X] T001 Create the `deploy/wine-docker-prebuilt/` directory and a `deploy/wine-docker-prebuilt/tests/` subdirectory (leave `deploy/wine-docker/` untouched per FR-008)
- [X] T002 [P] Add a placeholder `deploy/wine-docker-prebuilt/.gitkeep` (or first file) so the new tree is tracked; confirm no file under `mt5_grpc_server/`, `mt5_grpc_proto/`, or `protos/` is modified by this feature

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Produce the prebuilt image itself — the artifact every user story depends on. Without a self-contained image that bakes Python + VC++ + MT5 + server into `/wineprefix`, no story can run.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T003 Create the prebuilt Dockerfile skeleton in `deploy/wine-docker-prebuilt/Dockerfile`: `FROM ubuntu:24.04`, `ENV WINEARCH=win64 WINEPREFIX=/wineprefix DISPLAY=:99`, and build ARGs `PYTHON_VERSION=3.11.9`, `NUMPY_SPEC=numpy<2`, `MT5_SETUP_URL=<MetaQuotes CDN>` per contracts/build-args.md; install the same apt deps as `deploy/wine-docker/Dockerfile` (ca-certificates, cabextract, unzip, wget, winetricks, xvfb, wine, wine64, wine32, winbind) and `dpkg --add-architecture i386`
- [X] T004 In `deploy/wine-docker-prebuilt/Dockerfile`, `COPY mt5_grpc_proto` and `mt5_grpc_server` into `/app` and download the pinned Windows Python installer to `/opt/installers/python-installer.exe` using `${PYTHON_VERSION}` (mirror `deploy/wine-docker/Dockerfile` lines 25–31)
- [X] T005 In `deploy/wine-docker-prebuilt/Dockerfile`, add build-time `RUN` layers that bake the prefix by lifting `deploy/wine-docker/entrypoint.sh` first-run steps (lines 49–71) into build: `wineboot --init` + Wine version reg key, `winetricks -q vcrun2022` + `wineserver -w`, silent Windows Python install to `C:\Python311`, then pip `upgrade pip/setuptools/wheel`, `--force-reinstall ${NUMPY_SPEC}`, and `pip install -e` the two `/app` packages (use headless `Xvfb`/`DISPLAY` during build as needed). Do **not** add a `VOLUME` instruction (FR-003, build-args.md invariant 3)
- [X] T006 In `deploy/wine-docker-prebuilt/Dockerfile`, add a build-time `RUN` that downloads `${MT5_SETUP_URL}` and runs `wine mt5setup.exe /auto` under Xvfb (lift entrypoint.sh lines 73–87), then a **build-time invariant check** that fails the build (non-zero exit) if `/wineprefix/drive_c/Program Files/MetaTrader 5/terminal64.exe` is absent (build-args.md invariant 1, FR-009/FR-010, "build-time source unavailable" edge case)
- [X] T007 Create the runtime-only entrypoint in `deploy/wine-docker-prebuilt/entrypoint.sh`: keep ONLY the runtime portion of `deploy/wine-docker/entrypoint.sh` — Xvfb start + cleanup trap (lines 4–19), autostart INI generation with optional `[Common]` login block and `[Experts]` section (lines 97–117), MT5 launch (lines 119–120), and `exec wine python -m mt5_grpc_server.grpc_server --host/--port [--verbose]` (lines 122–134). Remove ALL install/download logic (markers, vcrun, python install, pip, MT5 download/wait) so nothing installs at runtime (FR-002, container-env.md), preserving `GRPC_VERBOSE` default and redaction behavior (FR-011)
- [X] T008 Wire the entrypoint into `deploy/wine-docker-prebuilt/Dockerfile`: `COPY deploy/wine-docker-prebuilt/entrypoint.sh` to `/usr/local/bin/mt5-grpc-entrypoint`, `chmod +x`, `EXPOSE 50051`, set `ENTRYPOINT`

**Checkpoint**: `docker build -f deploy/wine-docker-prebuilt/Dockerfile .` succeeds and produces an image whose `/wineprefix` already contains Python, VC++, the packages, and `terminal64.exe`. User story work can now begin.

---

## Phase 3: User Story 1 - Deploy a ready-to-run image with no first-run bootstrap (Priority: P1) 🎯 MVP

**Goal**: A single self-contained container starts serving gRPC quickly with no runtime install/download and no mounted volume.

**Independent Test**: Build/obtain the prebuilt image, start one container with valid credentials, and confirm the gRPC server answers shortly after start with no install/download step in the logs, no volume mounted, and that it still starts with outbound installer access blocked.

### Tests for User Story 1 (packaging change) ⚠️

> **NOTE: Write these tests FIRST; they should FAIL until the image + entrypoint behave correctly**

- [X] T009 [P] [US1] Create a build smoke test in `deploy/wine-docker-prebuilt/tests/test_build.sh` that runs `docker build -f deploy/wine-docker-prebuilt/Dockerfile` with default ARGs and asserts the build succeeds and `terminal64.exe` exists in the resulting image (SC-006, build invariant)
- [X] T010 [P] [US1] Create a single-container startup test in `deploy/wine-docker-prebuilt/tests/test_single_container.sh` that starts one container (no volume), asserts gRPC is ready in < 60s (SC-001), asserts `docker logs` contain NO Python/VC++/pip/MT5 install-or-download lines (FR-002, US1 scenario 1), and asserts `docker inspect` shows zero volume mounts (FR-003/SC-003, US1 scenario 2)
- [X] T011 [P] [US1] Add an offline-start assertion to `deploy/wine-docker-prebuilt/tests/test_single_container.sh` (or a sibling `test_offline_start.sh`): run the container with outbound access to installer sources blocked and confirm it still starts and serves (US1 scenario 3)

### Implementation for User Story 1

- [X] T012 [US1] Create `deploy/wine-docker-prebuilt/docker-compose.yml`: single service building from the prebuilt Dockerfile, image `ghcr.io/ins-enco/mt5-grpc-server-prebuilt:latest`, published `127.0.0.1:50051:50051` (FR-013), `shm_size: 1gb`, `restart: unless-stopped`, runtime env only (`GRPC_HOST/GRPC_PORT/GRPC_VERBOSE`, `MT5_LOGIN/MT5_PASSWORD/MT5_SERVER`, `MT5_STARTUP_DELAY`), and **no `volumes:` section** (FR-003, container-env.md)
- [X] T013 [US1] Verify the runtime env contract in `deploy/wine-docker-prebuilt/entrypoint.sh` honors exactly the variables in contracts/container-env.md (`MT5_LOGIN/PASSWORD/SERVER`, `GRPC_HOST/PORT/VERBOSE`, `MT5_STARTUP_DELAY`, `DISPLAY`, `XVFB_SCREEN`, `WINEPREFIX`) and that build-only vars (`MT5_SETUP_URL`, `MT5_INSTALL_TIMEOUT`, `NUMPY_SPEC`) have no runtime effect (FR-002)

**Checkpoint**: One prebuilt container starts, serves gRPC < 60s, mounts no volume, and needs no runtime install — US1 is independently demonstrable (MVP).

---

## Phase 4: User Story 2 - Run one isolated container per login on a single host (Priority: P1)

**Goal**: Bring up multiple per-login containers via a parameterized launcher, each with its own login, host port, and writable layer, fully isolated from each other.

**Independent Test**: Use the launcher to start two containers with different logins and ports, connect a client to each, confirm each serves its own account; stop one and confirm the other keeps serving; re-run with an in-use port and confirm a clear failure.

### Tests for User Story 2 (packaging change) ⚠️

- [X] T014 [P] [US2] Create launcher argument/validation tests in `deploy/wine-docker-prebuilt/tests/test_launcher_args.sh`: missing `--login` or `--port` exits `2`; non-numeric/out-of-range port exits `2`; existing container name exits `3`; `--help` prints usage (launcher-cli.md exit codes + behavior contract 1–2)
- [X] T015 [P] [US2] Create a multi-container isolation test in `deploy/wine-docker-prebuilt/tests/test_multi_login.sh`: launch two logins on two ports, assert each serves independently, assert `docker rm -f` of one leaves the other serving (SC-004), and assert neither container has a volume mount (FR-003/FR-004)
- [X] T016 [P] [US2] Add a port-collision assertion to `deploy/wine-docker-prebuilt/tests/test_launcher_args.sh` (or `test_port_collision.sh`): launching a second container on a host port already in use fails clearly with non-zero exit and does not hijack the first endpoint ("host port collision" edge case, launcher-cli.md behavior contract 3)

### Implementation for User Story 2

- [X] T017 [US2] Create the bash launcher `deploy/wine-docker-prebuilt/run-login.sh` implementing the full launcher-cli.md surface: flags `--login`, `--port`, `--password`, `--server`, `--name`, `--image`, `--bind` (default `127.0.0.1`), `--verbose` (default `true`), `--shm-size` (default `1gb`), `-h/--help`; validate required/numeric args (exit `2`); refuse existing container name (exit `3`); run `docker run -d` with `--restart unless-stopped`, `-p <bind>:<port>:50051`, per-login env vars, and **no volume**; surface Docker's port-in-use error verbatim (FR-016, FR-006, FR-007, FR-013)
- [X] T018 [P] [US2] Create the optional PowerShell launcher `deploy/wine-docker-prebuilt/run-login.ps1` mirroring the same flag surface, defaults, validation, and exit-code semantics as `run-login.sh` for Windows hosts (research.md Decision 5)

**Checkpoint**: Dozens of per-login containers can be launched with distinct ports/credentials, each isolated with its own writable layer; failure/removal of one does not affect others — US2 is independently demonstrable.

---

## Phase 5: User Story 3 - Choose between deployment options (Priority: P2)

**Goal**: Documentation lets an operator pick between the bootstrap and prebuilt options and bring up multiple per-login containers without reading source.

**Independent Test**: Following only the docs, launch each deployment option and bring up multiple per-login containers successfully in under 15 minutes (SC-005).

### Tests for User Story 3

- [X] T019 [P] [US3] Create a docs-walkthrough validation checklist `deploy/wine-docker-prebuilt/tests/test_quickstart_walkthrough.md` (or script) that steps through quickstart.md sections A–D and records pass/fail against SC-001..SC-006 (SC-005 "docs alone" criterion)

### Implementation for User Story 3

- [X] T020 [P] [US3] Write `deploy/wine-docker-prebuilt/README.md`: describe the prebuilt option, its trade-offs vs. bootstrap (data-model.md "Deployment option" table), build/pull steps, single-container run, multi-login launcher usage with distinct ports/credentials, ephemeral-writable-layer and baked-MT5-self-update behaviors, and host-resource/`--shm-size` guidance (FR-012, spec US3, edge cases)
- [X] T021 [US3] Edit the root `README.md` to present BOTH deployment options (bootstrap `deploy/wine-docker/` and prebuilt `deploy/wine-docker-prebuilt/`) with a short comparison and links to each README, confirming the bootstrap docs remain accurate/unchanged (FR-008, FR-012)

**Checkpoint**: An operator can choose and launch either option and multiple per-login containers from docs alone — all three stories independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Release both images to GHCR (private) and finalize reproducibility/security posture across stories.

- [X] T022 Edit `.github/workflows/docker-ghcr.yml` to build and push BOTH images as distinct artifacts: keep the existing `mt5-grpc-server` (bootstrap, `deploy/wine-docker/Dockerfile`, unchanged) and add a second build/push of `mt5-grpc-server-prebuilt` (`deploy/wine-docker-prebuilt/Dockerfile`) with its own metadata/tags (FR-014, research.md Decision 6)
- [X] T023 [P] Document in `deploy/wine-docker-prebuilt/README.md` the requirement that BOTH GHCR packages be set **private** (authentication required to pull), noting the prebuilt package MUST stay private because it redistributes the MT5 terminal (FR-015)
- [X] T024 [P] Document the strict-reproducibility guidance (override `MT5_SETUP_URL` with a pinned internal mirror) in `deploy/wine-docker-prebuilt/README.md`, consistent with quickstart.md and build-args.md (FR-010, SC-006)
- [ ] T025 Run the quickstart.md verification checklist end-to-end (SC-001 ready <60s / no install lines, SC-002 dozens isolated, SC-003 zero volumes, SC-004 one-down-others-up, SC-005 docs-in-15-min, SC-006 rebuild-equivalent) and record results
- [X] T026 [P] Final Constitution re-check: confirm zero changes under `protos/`, `mt5_grpc_proto/`, `mt5_grpc_server/`, and that `deploy/wine-docker/` (bootstrap) is byte-unchanged (Constitution Check PASS, FR-008)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - start immediately
- **Foundational (Phase 2)**: Depends on Setup - BLOCKS all user stories (the image must exist)
- **User Stories (Phase 3–5)**: All depend on Foundational (Phase 2) completion
  - US1 (P1) and US2 (P1) can then proceed in parallel; US3 (P2) documents both
- **Polish (Phase 6)**: Depends on the desired user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: Needs the prebuilt image (Phase 2). No dependency on other stories.
- **User Story 2 (P1)**: Needs the prebuilt image (Phase 2) and its image name. Independent of US1 (launcher targets the image directly), but shares the same image.
- **User Story 3 (P2)**: Documents both US1 and US2; most useful after both, but the prebuilt README (T020) can be drafted in parallel.

### Within Each User Story

- Tests written first and expected to FAIL before implementation
- Dockerfile layers (Phase 2) before compose/launcher that run the image
- Launcher script before its argument/collision tests pass
- Story complete before moving to next priority

### Parallel Opportunities

- Setup: T002 [P] after T001
- US1 tests T009/T010/T011 [P] (different files) before/independent of impl
- US2 tests T014/T015/T016 [P]; launcher variants T017 + T018 [P]
- US3 T020 [P] can start while US1/US2 finish; polish T023/T024/T026 [P]
- Both P1 stories (US1, US2) can be built in parallel once Phase 2 is done

---

## Parallel Example: User Story 1

```bash
# Launch all US1 tests together (different files):
Task: "Build smoke test in deploy/wine-docker-prebuilt/tests/test_build.sh"
Task: "Single-container startup test in deploy/wine-docker-prebuilt/tests/test_single_container.sh"
Task: "Offline-start assertion in deploy/wine-docker-prebuilt/tests/test_offline_start.sh"
```

## Parallel Example: User Story 2

```bash
# Launcher variants and tests in parallel (different files):
Task: "bash launcher deploy/wine-docker-prebuilt/run-login.sh"
Task: "PowerShell launcher deploy/wine-docker-prebuilt/run-login.ps1"
Task: "Launcher arg tests deploy/wine-docker-prebuilt/tests/test_launcher_args.sh"
Task: "Multi-login isolation test deploy/wine-docker-prebuilt/tests/test_multi_login.sh"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (the prebuilt image — CRITICAL, blocks all stories)
3. Complete Phase 3: User Story 1
4. **STOP and VALIDATE**: one container serves gRPC < 60s, no install lines, no volume
5. Deploy/demo the fast-start immutable single container

### Incremental Delivery

1. Setup + Foundational (image) → Foundation ready
2. US1 → single prebuilt container validated → Demo (MVP!)
3. US2 → per-login launcher + multi-container isolation → Demo
4. US3 → both options documented → Demo
5. Polish → CI publishes both private GHCR images + reproducibility/security docs

### Parallel Team Strategy

Once Phase 2 (image) is done:
- Developer A: US1 (single-container compose + smoke/offline tests)
- Developer B: US2 (launcher scripts + isolation/collision tests)
- Developer C: US3 docs + Phase 6 CI workflow

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to a specific user story for traceability
- No `.proto`/RPC/server/client code changes (Constitution PASS); `deploy/wine-docker/` stays unchanged (FR-008)
- Verify deployment tests fail before implementing the corresponding artifact
- Commit after each task or logical group
- Stop at any checkpoint to validate a story independently
