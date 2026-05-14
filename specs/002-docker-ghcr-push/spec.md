# Feature Specification: Build and Publish Docker Image to GHCR

**Feature Branch**: `002-docker-ghcr-push`  
**Created**: 2026-05-14  
**Status**: Draft  
**Input**: User description: "Build the Docker image and push it to a registry (GHCR). The Docker image uses Wine on Ubuntu to run Windows Python + MT5."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Publish a New Release Image (Priority: P1)

A maintainer tags a new release on the repository and expects the container image to be automatically built and published to GHCR under that release tag, so consumers can pull a stable, versioned image without building locally.

**Why this priority**: Publishing versioned images is the primary deliverable. Without it, downstream consumers have no way to use the image without a local build.

**Independent Test**: Can be fully tested by creating a git tag, verifying the workflow completes, and confirming `docker pull ghcr.io/<owner>/mt5-grpc-server:<tag>` succeeds.

**Acceptance Scenarios**:

1. **Given** a new git tag (e.g., `v1.0.0`) is pushed to the repository, **When** the automated workflow completes, **Then** the image `ghcr.io/<owner>/mt5-grpc-server:v1.0.0` is publicly available on GHCR.
2. **Given** a release tag is pushed, **When** the workflow completes, **Then** the image is also tagged as `latest` on GHCR.
3. **Given** the build fails (e.g., Dockerfile error), **When** the workflow runs, **Then** no image is pushed and the failure is reported clearly.

---

### User Story 2 - Continuous Integration Build on Main Branch (Priority: P2)

A contributor merges a pull request to the main branch. The automated workflow builds the image to verify it is still buildable and pushes a development snapshot image so integration testers can pull the latest state.

**Why this priority**: Provides early feedback on broken builds and gives testers access to the latest main-branch image without a local Docker build environment.

**Independent Test**: Can be tested by merging to main, confirming the workflow triggers, and running `docker pull ghcr.io/<owner>/mt5-grpc-server:main`.

**Acceptance Scenarios**:

1. **Given** a commit is pushed to the `main` branch, **When** the build workflow runs, **Then** the image is published to GHCR tagged as `main`.
2. **Given** the build succeeds, **When** inspecting the published image metadata on GHCR, **Then** the source commit SHA is traceable via image labels or tags.

---

### User Story 3 - Pull and Run the Image Without a Local Build (Priority: P3)

A developer or operator wants to run the MT5 gRPC server on a Linux host without cloning the repository or building the Docker image locally.

**Why this priority**: Reduces setup friction for consumers of the gRPC server. Depends on P1 being complete.

**Independent Test**: Can be tested by running `docker pull ghcr.io/<owner>/mt5-grpc-server:latest` on a clean machine and verifying the container starts successfully.

**Acceptance Scenarios**:

1. **Given** the image is published to GHCR, **When** a user pulls and runs the container with the documented `docker compose` configuration, **Then** the gRPC server starts and accepts connections on the configured port.
2. **Given** the image is pulled from GHCR, **When** the container starts, **Then** no local source code or build tooling is required.

---

### Edge Cases

- What happens when a git tag is pushed but the Dockerfile has a syntax error?
- How does the workflow behave if GHCR authentication credentials are missing or expired?
- What happens if the same tag is pushed again (re-tagging an existing release)?
- How is the build handled if external dependencies (Wine base image, Python installer, MT5 installer URL) are temporarily unavailable?
- What is the behavior when the image layer cache is cold (first build or cache eviction)?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST build the Docker image defined in `deploy/wine-docker/Dockerfile` using the repository root as the build context.
- **FR-002**: The system MUST tag the published image with the git tag name when triggered by a version tag push (e.g., `v1.2.3`).
- **FR-003**: The system MUST tag the published image as `latest` whenever a release tag is published.
- **FR-004**: The system MUST tag the published image with the branch name (e.g., `main`) when triggered by a push to the main branch.
- **FR-005**: The system MUST include the git commit SHA as an image label or additional tag so each published image is traceable to its source commit.
- **FR-006**: The system MUST authenticate to GHCR using the repository's built-in GitHub Actions token, requiring no manual secrets configuration for standard pushes.
- **FR-007**: The system MUST NOT push an image if the build step fails.
- **FR-008**: The system MUST produce a build log accessible to repository maintainers for debugging failed builds.
- **FR-009**: The build workflow MUST be triggered automatically on pushes to the `main` branch and on version tag pushes matching the pattern `v*.*.*`.

### Protocol and MT5 Contract Impact *(mandatory)*

- **Affected RPCs/Messages**: None — this feature adds CI/CD automation and does not modify any proto services, RPCs, messages, or field definitions.
- **Compatibility Decision**: Behavior-only change; no protocol changes required.
- **MT5 Operation Mapping**: None — no new MT5 operations are introduced.
- **Cross-Language Type Notes**: None.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A published image is available on GHCR within 30 minutes of a triggering git event (tag push or main branch push) completing successfully.
- **SC-002**: A consumer can pull and start the container from GHCR on a machine with no local copy of the repository.
- **SC-003**: Every published image can be traced back to its exact source commit within the repository.
- **SC-004**: Build failures are surfaced within the repository's CI status checks, providing actionable error output to the contributor.
- **SC-005**: No manual credentials management is required for the automated build-and-push workflow under normal operating conditions.

## Assumptions

- The repository is hosted on GitHub and has access to GitHub Actions for CI/CD automation.
- GHCR is used as the target registry; the image namespace follows the pattern `ghcr.io/<github-owner>/<repo-name>`.
- The target platform for the image is `linux/amd64`; multi-architecture builds (e.g., ARM64) are out of scope because Wine/MT5 require x86-64.
- The MT5 installer download URL (`MT5_SETUP_URL`) may change over time and is provided at runtime via environment variable, not baked into the image.
- Build cache is desirable to reduce build times but is not required for correctness.
- Image visibility on GHCR defaults to the same visibility as the repository (public if the repo is public).
- The Python installer embedded in the image (`/opt/installers/python-installer.exe`) is handled by the existing Dockerfile; this feature does not change how it is sourced.
