---
description: "Task list for Build and Publish Docker Image to GHCR"
---

# Tasks: Build and Publish Docker Image to GHCR

**Input**: Design documents from `/specs/002-docker-ghcr-push/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/docker-image-contract.md

**Tests**: No automated test files are generated for this feature. The deliverable is a
GitHub Actions workflow YAML; correctness is verified by triggering the workflow and
observing GHCR publish outcomes. Manual validation checkpoints are included at each
story phase.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Exact file paths included in each description

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the workflow file skeleton before adding any triggers or steps.

- [X] T001 Create `.github/workflows/docker-ghcr.yml` with workflow name `"Docker Build and Push to GHCR"`, empty `on:` block, and one job `build-and-push` with `runs-on: ubuntu-latest` and empty `steps: []`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared steps that every trigger path depends on — must be complete before
any user story can produce a working workflow run.

**⚠️ CRITICAL**: US1, US2, and US3 validation all require these steps in place.

- [X] T002 Add workflow-level `permissions: contents: read` and `packages: write` block to `.github/workflows/docker-ghcr.yml` (satisfies FR-006 — no PAT required)
- [X] T003 [P] Add `uses: actions/checkout@v4` step (name: "Checkout repository") to job steps in `.github/workflows/docker-ghcr.yml`
- [X] T004 [P] Add `uses: docker/login-action@v3` step (name: "Log in to GHCR", registry: `ghcr.io`, username: `${{ github.actor }}`, password: `${{ secrets.GITHUB_TOKEN }}`) in `.github/workflows/docker-ghcr.yml`

**Checkpoint**: Foundation ready — checkout and GHCR login in place. User story triggers can now be added.

---

## Phase 3: User Story 1 — Publish a New Release Image (Priority: P1) 🎯 MVP

**Goal**: A version tag push (`v*.*.*`) produces images tagged with the version number
and `latest` on GHCR, within 30 min of the push (SC-001).

**Independent Test**: Push tag `v0.0.1-smoke` to the repository, wait for the workflow to
complete, then verify both `docker pull ghcr.io/ins-enco/mt5-grpc-server:v0.0.1-smoke`
and `docker pull ghcr.io/ins-enco/mt5-grpc-server:latest` succeed. Delete the tag
after validation.

### Implementation for User Story 1

- [X] T005 [US1] Add `on: push: tags: ['v*.*.*']` trigger block to `.github/workflows/docker-ghcr.yml`
- [X] T006 [US1] Add `uses: docker/metadata-action@v5` step (id: `meta`, name: "Extract Docker metadata") with `images: ghcr.io/${{ github.repository_owner }}/mt5-grpc-server`, tags `type=semver,pattern={{version}}` and `type=semver,pattern={{major}}.{{minor}}`, and `flavor: latest=auto` in `.github/workflows/docker-ghcr.yml` (satisfies FR-002, FR-003, FR-005)
- [X] T007 [US1] Add `uses: docker/build-push-action@v6` step (name: "Build and push Docker image") with `context: .`, `file: deploy/wine-docker/Dockerfile`, `push: true`, `tags: ${{ steps.meta.outputs.tags }}`, `labels: ${{ steps.meta.outputs.labels }}`, `no-cache: true`, `platforms: linux/amd64` in `.github/workflows/docker-ghcr.yml` (satisfies FR-001, FR-007, FR-008)

**Checkpoint**: US1 complete — tag push produces `v1.2.3`, `1.2`, and `latest` on GHCR.
Validate via independent test before proceeding.

---

## Phase 4: User Story 2 — Continuous Integration Build on Main Branch (Priority: P2)

**Goal**: Push to `main` produces a `main`-tagged image on GHCR with each published image
traceable to its source commit SHA.

**Independent Test**: Push a commit to `main`, wait for the workflow, then verify
`docker pull ghcr.io/ins-enco/mt5-grpc-server:main` succeeds and
`docker inspect ghcr.io/ins-enco/mt5-grpc-server:main --format '{{ index .Config.Labels "org.opencontainers.image.revision" }}'`
returns the expected commit SHA.

### Implementation for User Story 2

- [X] T008 [US2] Add `branches: [main]` to the existing `on.push` block alongside the tags trigger in `.github/workflows/docker-ghcr.yml` (satisfies FR-009, FR-004)
- [X] T009 [US2] Add `type=ref,event=branch` tag rule to the existing `docker/metadata-action@v5` step in `.github/workflows/docker-ghcr.yml` so branch pushes produce the `main` tag (satisfies FR-004); note that `org.opencontainers.image.revision` OCI label is injected automatically — no additional configuration needed (satisfies FR-005)

**Checkpoint**: US2 complete — main branch pushes produce `main`-tagged image on GHCR
with traceable SHA. Validate via independent test before proceeding.

---

## Phase 5: User Story 3 — Pull and Run Without a Local Build (Priority: P3)

**Goal**: A consumer on a clean `linux/amd64` machine can pull the image from GHCR and
start the gRPC server using the documented compose configuration, with no local source
code or build tooling required.

**Independent Test**: On a machine with Docker installed and no local clone, run
`docker pull ghcr.io/ins-enco/mt5-grpc-server:latest`, then `docker compose up` using
the updated `deploy/wine-docker/docker-compose.yml` (with `MT5_SETUP_URL` set), and
confirm the gRPC server starts and accepts connections on port 50051.

### Implementation for User Story 3

- [X] T010 [US3] Update `image:` field in `deploy/wine-docker/docker-compose.yml` from `mt5-grpc-server:wine` to `ghcr.io/ins-enco/mt5-grpc-server:latest` so `docker compose pull` fetches from GHCR; the existing `build:` section remains for local development builds
- [X] T011 [P] [US3] Verify pull commands in `specs/002-docker-ghcr-push/quickstart.md` match the `image:` value set in T010 and the tag naming produced by the workflow

**Checkpoint**: US3 complete — consumers can pull and run without a local clone or build.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final validation and repository hygiene tasks.

- [X] T012 [P] Validate `.github/workflows/docker-ghcr.yml` YAML and action step syntax using `actionlint` (install: `choco install actionlint` or download from GitHub releases) or push to a fork to trigger GitHub's built-in workflow lint
- [X] T013 [P] Add Docker CI status badge to `README.md` using URL `https://github.com/ins-enco/Metatrader5-gRPC-server/actions/workflows/docker-ghcr.yml/badge.svg` pointing to the workflow run page

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 (file must exist before adding content)
- **US1 (Phase 3)**: Depends on Phase 2 — adds trigger + metadata + build steps
- **US2 (Phase 4)**: Depends on Phase 3 — adds branch trigger to existing workflow; shares the same metadata/build steps
- **US3 (Phase 5)**: Depends on Phase 3 completing successfully (image must be published) for validation
- **Polish (Phase 6)**: Depends on all story phases being complete

### User Story Dependencies

- **US1 (P1)**: Can start after Phase 2 — no dependency on US2 or US3
- **US2 (P2)**: Can start after US1 (adds to the same workflow file; cannot be parallelized with US1)
- **US3 (P3)**: Depends only on US1 (image must be published before pull validation can run)

### Within Each Phase

- Foundational: T003 and T004 can be written in parallel (independent steps)
- US3: T010 and T011 are independent files — parallel opportunity
- Polish: T012 and T013 are independent files — parallel opportunity

---

## Parallel Example: Foundational Phase

```bash
# Both tasks write independent sections of the workflow file (different steps):
Task T003: "Add checkout step to .github/workflows/docker-ghcr.yml"
Task T004: "Add GHCR login step to .github/workflows/docker-ghcr.yml"
```

## Parallel Example: User Story 3

```bash
# T010 edits docker-compose.yml; T011 verifies quickstart.md — different files:
Task T010: "Update image: field in deploy/wine-docker/docker-compose.yml"
Task T011: "Verify quickstart.md pull commands match workflow output"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001)
2. Complete Phase 2: Foundational (T002–T004)
3. Complete Phase 3: US1 (T005–T007)
4. **STOP and VALIDATE**: Push a smoke tag, confirm versioned + latest images appear on GHCR
5. Merge and ship — US1 alone satisfies the primary deliverable (SC-001, SC-002, SC-003, SC-005)

### Incremental Delivery

1. Setup + Foundational → workflow file with login/checkout
2. Add US1 → tag-triggered versioned + latest publishing (MVP)
3. Add US2 → main branch CI with SHA traceability
4. Add US3 → consumer compose file updated; documentation verified
5. Polish → badge + lint

### Single-Developer Sequence

Because US1 and US2 both modify the same workflow file, they must be done sequentially:

```
T001 → T002 → T003/T004 (parallel) → T005 → T006 → T007
     → T008 → T009
     → T010/T011 (parallel)
     → T012/T013 (parallel)
```

---

## Notes

- [P] tasks = different files, no shared state — safe to run in parallel
- [Story] label maps each task to its user story for traceability
- US1 and US2 share the same workflow file; implement sequentially within the file
- No automated test files are generated (GitHub Actions YAML is validated by running it)
- Commit after each phase or after each task to keep the branch clean
- The `no-cache: true` flag in T007 satisfies the spec clarification (always build from scratch)
- `docker/metadata-action@v5` injects the OCI `revision` label automatically; FR-005 requires no extra step
