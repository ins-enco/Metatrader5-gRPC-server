# Data Model: Build and Publish Docker Image to GHCR

**Branch**: `002-docker-ghcr-push` | **Date**: 2026-05-14

This feature introduces no application data model changes (no proto, database,
or in-memory state changes). The "model" for a CI/CD pipeline feature is the
workflow trigger schema, the image tag naming rules, and the OCI label schema.

---

## Workflow Trigger Events

| Entity | Field | Values |
|--------|-------|--------|
| `push` event | `branches` | `[main]` |
| `push` event | `tags` | `['v*.*.*']` (glob pattern) |

State transitions:

```
git push origin main         →  workflow: branch-push job
git push origin v1.2.3       →  workflow: tag-push job (same job, different input)
git push origin v1.2.3-rc1   →  workflow: tag-push job (pre-release, no `latest`)
```

---

## Image Tag Schema

Tags produced per trigger event:

| Trigger | Tag(s) | Required by |
|---------|--------|-------------|
| Push to `main` | `main` | FR-004 |
| Push of `v1.2.3` | `v1.2.3`, `1.2`, `latest` | FR-002, FR-003 |
| Push of `v1.2.3-rc1` | `v1.2.3-rc1` | FR-002 |

Full image reference format: `ghcr.io/<owner>/mt5-grpc-server:<tag>`

Where `<owner>` = `github.repository_owner` (resolved at workflow runtime).

---

## OCI Image Labels

Labels injected automatically by `docker/metadata-action@v5`:

| Label | Value | Purpose |
|-------|-------|---------|
| `org.opencontainers.image.title` | `mt5-grpc-server` | Human-readable name |
| `org.opencontainers.image.revision` | Full commit SHA | FR-005 traceability |
| `org.opencontainers.image.source` | Repository URL | Provenance |
| `org.opencontainers.image.created` | ISO-8601 timestamp | Build timestamp |
| `org.opencontainers.image.version` | Tag or branch name | Version reference |

---

## Workflow Job State

```
Trigger received
    │
    ▼
checkout (actions/checkout@v4)
    │
    ▼
login to ghcr.io (docker/login-action@v3)
    │
    ▼
generate tags + labels (docker/metadata-action@v5)
    │
    ▼
build Docker image (docker/build-push-action@v6)
    │
    ├── build fails → job fails, NO push (FR-007)
    │
    └── build succeeds → push image(s) to GHCR
```

---

## Validation Rules

- Tag pattern `v*.*.*` must be a valid semver prefix; non-conforming tags do not
  trigger the workflow.
- The Dockerfile path `deploy/wine-docker/Dockerfile` and build context (repo root)
  must exist at the SHA being built; a missing Dockerfile is a build failure (FR-007).
- `GITHUB_TOKEN` must have `packages: write` scope; this is granted by the
  workflow-level `permissions` block and requires no manual configuration (FR-006).
