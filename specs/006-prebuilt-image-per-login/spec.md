# Feature Specification: Prebuilt Self-Contained Image with Per-Login Containers

**Feature Branch**: `006-prebuilt-image-per-login`  
**Created**: 2026-07-08  
**Status**: Draft  
**Input**: User description: "I want to add one more option when deploying Docker: another way to build the image — a prebuilt image that already includes Python + VC++ + the MT5 terminal + a gRPC server inside. Because I want to run multiple containers on one machine, one for each login, I will no longer mount a shared volume. Instead, each container will have its own writable layer."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Deploy a ready-to-run image with no first-run bootstrap (Priority: P1)

An operator pulls or builds a single self-contained image that already includes everything the server needs — the Python runtime, the Microsoft VC++ runtime, the MetaTrader 5 terminal, and the gRPC server. When they start a container, it begins serving gRPC requests quickly, without the lengthy first-start installation the existing deployment performs.

**Why this priority**: The core of the request. Today's deployment does its heavy setup (install runtime, packages, download and install MT5) on first container start, which is slow and network-dependent. A prebuilt, immutable image removes that first-run cost and makes startup predictable and reproducible. This alone delivers value even without the multi-container scenario.

**Independent Test**: Build/obtain the prebuilt image, start one container with valid credentials, and confirm the gRPC server answers a request shortly after start without running any install/download step at runtime.

**Acceptance Scenarios**:

1. **Given** the prebuilt image, **When** a container is started with valid login credentials, **Then** the gRPC server accepts requests without performing runtime installation of Python, the VC++ runtime, packages, or MetaTrader 5.
2. **Given** the prebuilt image, **When** a container is started with no volume mounted, **Then** it operates normally using only its own writable layer.
3. **Given** the prebuilt image, **When** the container has no outbound access to installer sources at runtime, **Then** it still starts and serves requests (everything needed is already inside the image).

---

### User Story 2 - Run one isolated container per login on a single host (Priority: P1)

An operator runs several containers on the same machine, one per MetaTrader 5 login. Each container has its own credentials, its own published host port, and its own isolated writable layer. No shared volume is mounted, so containers do not contend for or corrupt each other's state.

**Why this priority**: This is the operator's stated deployment goal and the reason the shared volume is being dropped. Per-login isolation via each container's own writable layer is what makes multi-account operation on one host safe and simple.

**Independent Test**: Start two or more containers from the prebuilt image with different logins and different host ports, connect a client to each, and confirm each returns data for its own account with no cross-interference.

**Acceptance Scenarios**:

1. **Given** two containers started with different logins and different host ports, **When** a client connects to each, **Then** each serves its own account's data independently.
2. **Given** multiple running per-login containers, **When** one container is stopped, recreated, or fails, **Then** the other containers continue serving without disruption.
3. **Given** multiple containers on one host, **When** they run concurrently, **Then** no shared or persistent volume is required and each keeps its runtime state in its own writable layer.

---

### User Story 3 - Choose between deployment options (Priority: P2)

An operator reviewing how to deploy can see both options — the existing bootstrap image (thin image plus shared persistent prefix) and the new prebuilt self-contained image — with guidance on when to use each and how to run multiple per-login containers.

**Why this priority**: The prebuilt option is additive, not a replacement. Clear documentation lets operators pick the option that fits their situation (fast/immutable/multi-tenant vs. small image with persistent shared state) and avoids confusion between the two.

**Independent Test**: Follow the documentation to launch each option and to bring up multiple per-login containers, confirming the docs are sufficient to succeed without reading source.

**Acceptance Scenarios**:

1. **Given** the deployment documentation, **When** an operator reads it, **Then** both options and their trade-offs are described, including how to assign distinct ports and credentials per login.
2. **Given** the existing bootstrap deployment, **When** the prebuilt option is added, **Then** the existing option still works unchanged.

---

### Edge Cases

- **Host port collision**: Starting a second per-login container on a host port already in use must fail clearly rather than silently taking over another login's endpoint.
- **One login fails**: A failed or invalid login in one container must not affect other running per-login containers.
- **Container recreation loses state**: Because state lives in the writable layer (not a persistent volume), recreating a container resets its per-login runtime state (cached terminal data, logs); the container must re-establish its session from configuration on next start.
- **Baked MT5 becomes outdated**: The MetaTrader 5 terminal baked into the image may lag the broker's required minimum version and may attempt to self-update at start; any update is written only to that container's writable layer and is lost on recreation.
- **Build-time source unavailable**: If the MetaTrader 5 installer or Python source is unreachable when the prebuilt image is built, the build must fail clearly rather than produce an incomplete image.
- **Host resource pressure**: Running many per-login containers (each with its own terminal and headless display) on one host may exhaust memory/CPU/shared-memory; behavior under pressure should be understood and documented.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The project MUST offer a second Docker image build option that produces a self-contained image bundling the Python runtime, the Microsoft VC++ runtime, the MetaTrader 5 terminal, and the gRPC server at build time.
- **FR-002**: A container started from the prebuilt image MUST begin serving gRPC requests without performing any of the runtime installation steps (Python, VC++ runtime, server packages, MetaTrader 5 download/install) that the existing bootstrap image performs on first start.
- **FR-003**: The prebuilt image MUST operate without mounting any shared or persistent volume.
- **FR-004**: Each container MUST keep its runtime state (terminal profile/data, prefix state, logs) in its own writable layer, isolated from other containers on the same host.
- **FR-005**: The system MUST support running multiple containers concurrently on a single host, one per MetaTrader 5 login, each isolated from the others.
- **FR-006**: Each container MUST accept per-login configuration (login, password, server) so different containers connect to different MetaTrader 5 accounts.
- **FR-007**: Each container MUST be able to publish its gRPC endpoint on a distinct host port so multiple logins are independently reachable on one host.
- **FR-008**: The existing bootstrap deployment (thin image plus shared persistent prefix volume) MUST remain available and behave unchanged; the prebuilt option is additive.
- **FR-009**: Building the prebuilt image MUST obtain the MetaTrader 5 terminal at build time and produce an image that requires no MetaTrader 5 download at runtime.
- **FR-010**: The prebuilt image build MUST be reproducible from pinned inputs (e.g., pinned Python version and a defined MetaTrader 5 installer source) so an equivalent image can be rebuilt.
- **FR-011**: The prebuilt image MUST preserve the existing logging behavior, including verbose request/response logging and redaction of secret-like fields (password, token, secret, api_key, and similar).
- **FR-012**: Documentation MUST describe both deployment options, their trade-offs, and the procedure for running multiple per-login containers with distinct ports and credentials.
- **FR-013**: The endpoint of each container MUST default to a non-public binding (host-local) unless the operator explicitly chooses to expose it, consistent with the existing deployment's security posture.

### Protocol and MT5 Contract Impact *(mandatory)*

- **Affected RPCs/Messages**: None. This feature changes only how the server is packaged and deployed; it does not add, remove, or modify any proto services, RPCs, messages, fields, or field numbers.
- **Compatibility Decision**: Backward-compatible. Packaging/deployment-only change with no wire-format impact; existing clients and the existing bootstrap deployment are unaffected.
- **MT5 Operation Mapping**: Unchanged. The server communicates with the MetaTrader 5 terminal exactly as today; only the terminal's provisioning moves from first-run install to build-time bundling. Per-login containers each run their own terminal instance bound to one account.
- **Cross-Language Type Notes**: None. No changes to optional/repeated fields, time, numeric precision, or sentinel handling.

### Key Entities *(include if feature involves data)*

- **Prebuilt image**: A self-contained, immutable build artifact that already includes the Python runtime, VC++ runtime, MetaTrader 5 terminal, and gRPC server; requires no runtime install and no mounted volume.
- **Per-login container instance**: A running container bound to exactly one MetaTrader 5 account, with its own credentials, its own published host port, and its own isolated writable layer.
- **Deployment option**: The operator's choice between the existing bootstrap deployment (thin image + shared persistent prefix) and the new prebuilt self-contained deployment.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A container started from the prebuilt image is ready to accept gRPC requests in under 60 seconds, without any runtime install/download step (versus the multi-minute first-run bootstrap of the existing deployment).
- **SC-002**: An operator can run at least 5 independent per-login containers on a single host, each with distinct credentials and ports, and each serves its own account with no cross-interference.
- **SC-003**: The prebuilt deployment runs with zero shared or persistent volume mounts.
- **SC-004**: Stopping, recreating, or failing one per-login container has zero impact on the other running containers.
- **SC-005**: A new operator can choose and launch either deployment option, and bring up multiple per-login containers, by following the documentation alone in under 15 minutes.
- **SC-006**: Rebuilding the prebuilt image from the same pinned inputs yields a functionally equivalent image (no runtime download required in either build).

## Assumptions

- **Prebuilt option is additive**: The existing `deploy/wine-docker` bootstrap deployment remains the default reference; this feature adds a parallel prebuilt option rather than replacing it.
- **Writable-layer state is ephemeral by design**: Per the operator's explicit choice to drop the shared volume, per-login runtime state lives only in each container's writable layer and is not expected to persist across container recreation. If durable per-login state is needed later, attaching a per-container volume is a separate, out-of-scope enhancement.
- **MT5 obtained at build time**: The prebuilt image downloads/installs the MetaTrader 5 terminal during the image build (reasonable default), pinned to a defined installer source, so runtime needs no download.
- **Image distribution is the operator's responsibility**: Because the prebuilt image bundles the MetaTrader 5 terminal, the resulting image is intended for the operator's own/internal use; MetaTrader 5 redistribution/licensing terms must be respected and the prebuilt image should not be published to a public registry. This is documented as a caveat.
- **One account per container**: Each container serves exactly one MetaTrader 5 login; multi-account within a single container is out of scope.
- **Operator assigns ports and credentials**: The operator is responsible for giving each per-login container a unique host port and the correct per-account credentials.
- **Same runtime behavior**: Aside from provisioning, the prebuilt image runs the same server with the same configuration surface (host/port, verbose logging, redaction, algo-trading enablement) as the existing deployment.
- **Reuses existing technology base**: The prebuilt image is expected to build on the same Windows-runtime-under-Wine approach as the existing deployment, since the MetaTrader 5 Python integration is Windows-native.
