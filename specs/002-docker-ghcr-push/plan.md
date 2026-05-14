# Implementation Plan: Build and Publish Docker Image to GHCR

**Branch**: `002-docker-ghcr-push` | **Date**: 2026-05-14 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/002-docker-ghcr-push/spec.md`

## Summary

Add a GitHub Actions workflow that builds the Wine/Ubuntu Docker image from
`deploy/wine-docker/Dockerfile` and publishes it to GHCR
(`ghcr.io/ins-enco/mt5-grpc-server`) on every push to `main` and on version
tag pushes matching `v*.*.*`. Tags are managed by `docker/metadata-action`;
authentication uses the built-in `GITHUB_TOKEN`; no caching is used.

## Technical Context

**Language/Version**: GitHub Actions YAML; Docker image base: Ubuntu 24.04, Wine, Python 3.11.9
**Primary Dependencies**: `docker/login-action@v3`, `docker/metadata-action@v5`, `docker/build-push-action@v6`, `actions/checkout@v4`
**Storage**: GHCR — `ghcr.io/ins-enco/mt5-grpc-server`
**Testing**: N/A — workflow correctness is verified by the CI run itself; no unit tests apply to pipeline YAML
**Target Platform**: `linux/amd64` (Wine + MT5 require x86-64; multi-arch is out of scope)
**Project Type**: CI/CD pipeline (single GitHub Actions workflow file)
**Performance Goals**: Build and push within 30 min per SC-001
**Constraints**: No Docker layer caching (spec clarification); cold build on every trigger; `linux/amd64` only
**Scale/Scope**: One image published per triggering event; two trigger patterns (branch push to `main`, tag push `v*.*.*`)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Protocol contract**: PASS — No `.proto` files, RPCs, messages, or field definitions are
  modified. Spec section "Protocol and MT5 Contract Impact" explicitly states no protocol
  changes are required. Field numbers are untouched; no binding regeneration needed.

- **MT5 behavior fidelity**: PASS (N/A) — No MT5 operations are introduced or modified.
  The Docker image packages the existing server; all runtime MT5 semantics are unchanged.

- **Multi-language type safety**: PASS (N/A) — No public contract changes. The published
  image serves the existing gRPC interface; no cross-language type handling is affected.

- **Reliability and observability**: PASS — FR-008 ensures build logs are accessible to
  maintainers in GitHub Actions; FR-007 guarantees no image is pushed when the build
  fails; SC-004 surfaces failures through GitHub Actions status checks. Build time is
  bounded by SC-001 (≤30 min). No secrets or account credentials are logged.

- **Test and release reproducibility**: PASS — FR-006 uses the built-in `GITHUB_TOKEN`
  satisfying SC-005. The workflow YAML is committed to the repository, making the
  process reproducible from a clean checkout. Constitution V ("Packaging and release
  changes MUST be reproducible from repository scripts and documented commands") is met.

## Project Structure

### Documentation (this feature)

```text
specs/002-docker-ghcr-push/
├── plan.md                         # This file (/speckit-plan)
├── research.md                     # Phase 0 output (/speckit-plan)
├── data-model.md                   # Phase 1 output (/speckit-plan)
├── quickstart.md                   # Phase 1 output (/speckit-plan)
├── contracts/
│   └── docker-image-contract.md   # Phase 1 output (/speckit-plan)
└── tasks.md                        # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
.github/
└── workflows/
    └── docker-ghcr.yml    # NEW: Docker build and GHCR push workflow
```

No changes to:
- `deploy/wine-docker/Dockerfile` — consumed as-is; build context is the repo root
- `mt5_grpc_proto/`, `mt5_grpc_server/` — no application code changes
- `protos/` — no proto changes

**Structure Decision**: Single new file in `.github/workflows/`. The build context is the
repository root, matching the existing `docker-compose.yml` setting (`build.context: ../../`).

## Complexity Tracking

> No Constitution Check violations. Section intentionally empty.

## Implementation Detail

### Workflow: `.github/workflows/docker-ghcr.yml`

**Triggers**:
```yaml
on:
  push:
    branches: [main]
    tags: ['v*.*.*']
```

**Permissions** (minimum required):
```yaml
permissions:
  contents: read
  packages: write
```

**Steps in order**:

| Step | Action | Purpose |
|------|--------|---------|
| 1 | `actions/checkout@v4` | Full source checkout (build context) |
| 2 | `docker/login-action@v3` | Authenticate to `ghcr.io` using `GITHUB_TOKEN` (FR-006) |
| 3 | `docker/metadata-action@v5` | Generate image tags + OCI labels (FR-002, FR-003, FR-004, FR-005) |
| 4 | `docker/build-push-action@v6` | Build from `deploy/wine-docker/Dockerfile`, push on success (FR-001, FR-007) |

**Image name**: `ghcr.io/${{ github.repository_owner }}/mt5-grpc-server`

**Tag strategy**:

| Trigger | Tags produced |
|---------|--------------|
| Push to `main` | `main` |
| Push of `v1.2.3` tag | `v1.2.3`, `1.2.3`, `latest` |

SHA traceability is injected automatically by `docker/metadata-action` via the OCI label
`org.opencontainers.image.revision` (FR-005). No extra tag for SHA is needed.

**No-cache flag**: `no-cache: true` on `docker/build-push-action` (spec clarification: always build from scratch).

**Build failure** (FR-007): `docker/build-push-action` only pushes when the build step
exits 0. A failed build causes the job to fail before the push step executes.

**Build logs** (FR-008): GitHub Actions records all step stdout/stderr; logs are
accessible from the Actions tab for the duration of the log retention period.

## Post-Design Constitution Re-Check

All five gates remain PASS. No proto, MT5, or cross-language changes introduced.
The workflow design adds no new reliability risks beyond the bounds described in the spec.
No Complexity Tracking entries required.
