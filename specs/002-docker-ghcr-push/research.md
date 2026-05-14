# Research: Build and Publish Docker Image to GHCR

**Branch**: `002-docker-ghcr-push` | **Date**: 2026-05-14

The specification was clarified in the session of 2026-05-14. No open questions
remain. This file documents the decisions made during planning so that
implementation can proceed without revisiting alternatives.

---

## Decision 1 — GitHub Actions actions to use

**Decision**: Use the official Docker community actions:
- `actions/checkout@v4`
- `docker/login-action@v3`
- `docker/metadata-action@v5`
- `docker/build-push-action@v6`

**Rationale**: These are the canonical, maintained actions recommended by Docker
for GHCR publishing. They are already used in a large portion of public GitHub
Actions Docker workflows and have stable major-version pins.

**Alternatives considered**:
- `docker build` + `docker push` shell commands — rejected because `docker/metadata-action`
  handles all tag permutations and OCI label injection automatically, reducing
  implementation surface and error risk.
- `goreleaser` — rejected; over-engineered for a single-image pipeline.

---

## Decision 2 — Image name convention

**Decision**: `ghcr.io/${{ github.repository_owner }}/mt5-grpc-server`

**Rationale**: The spec user stories reference `ghcr.io/<owner>/mt5-grpc-server`
explicitly (User Stories 1–3). Using `github.repository_owner` avoids hardcoding
`ins-enco` and keeps the workflow portable if the repository is forked.

**Alternatives considered**:
- `ghcr.io/${{ github.repository }}` (e.g., `ghcr.io/ins-enco/metatrader5-grpc-server`) —
  rejected; does not match the image name in the spec user stories.

---

## Decision 3 — Authentication method

**Decision**: Use `GITHUB_TOKEN` with `packages: write` permission in the
workflow-level `permissions` block. No personal access token (PAT) or repository
secret is required for standard pushes.

**Rationale**: GHCR accepts the built-in `GITHUB_TOKEN` from the same repository.
The spec explicitly requires this (FR-006, SC-005). GitHub automatically grants
the token access to packages owned by the same organization/user.

**Alternatives considered**:
- Repository secret PAT — rejected per FR-006 and SC-005; also introduces a rotation
  maintenance burden.

---

## Decision 4 — Tag strategy

**Decision**: Use `docker/metadata-action@v5` with the following tag configuration:

```yaml
tags: |
  type=ref,event=branch
  type=semver,pattern={{version}}
  type=semver,pattern={{major}}.{{minor}}
```

Combined with a `flavor` block that sets `latest=auto` (adds `latest` only when
triggered by a semver tag that is not a pre-release). This produces:

| Trigger | Tags |
|---------|------|
| Push to `main` | `main` |
| Push of `v1.2.3` | `v1.2.3`, `1.2`, `latest` |
| Push of `v1.2.3-rc1` | `v1.2.3-rc1` (no `latest`) |

**Rationale**: Satisfies FR-002 (version tag), FR-003 (`latest` on release),
FR-004 (`main` on branch push). The `{{major}}.{{minor}}` tag lets consumers
pin to a minor line. Pre-release tags do not overwrite `latest`.

**Alternatives considered**:
- Manual tag list in shell — rejected; brittle and duplicates `docker/metadata-action`
  functionality.
- Only `latest` + SHA — rejected; FR-002 requires the exact version tag to be
  published.

---

## Decision 5 — Commit SHA traceability

**Decision**: Rely on the OCI label `org.opencontainers.image.revision` injected
automatically by `docker/metadata-action`. No additional image tag for the SHA.

**Rationale**: The spec (FR-005) requires the SHA to be "traceable via image labels
or tags." OCI labels satisfy this. Adding a separate SHA tag would clutter the tag
list on GHCR without additional benefit.

**Alternatives considered**:
- `type=sha` tag (e.g., `sha-abc1234`) — acceptable alternative; not chosen because
  the OCI label is sufficient per spec wording.

---

## Decision 6 — Layer caching

**Decision**: `no-cache: true` on `docker/build-push-action`.

**Rationale**: Spec clarification (2026-05-14) explicitly states: "No caching;
always build from scratch." SC-001 (≤30 min) is expected to be achievable
without caching given the spec assumption.

**Alternatives considered**:
- GitHub Actions cache (`cache-from: type=gha`) — rejected per spec clarification.
- Registry cache (`cache-from: type=registry`) — rejected per spec clarification.

---

## Decision 7 — Build platform

**Decision**: `platforms: linux/amd64` only (no multi-arch).

**Rationale**: Spec assumption: "The target platform for the image is `linux/amd64`;
multi-architecture builds (e.g., ARM64) are out of scope because Wine/MT5 require
x86-64." QEMU-based cross-compilation would also significantly increase build time.

**Alternatives considered**:
- `linux/amd64,linux/arm64` — rejected per spec scope definition.
