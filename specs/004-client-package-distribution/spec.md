# Feature Specification: Shared C# Client Package Distribution

**Feature Branch**: `004-client-package-distribution`  
**Created**: 2026-07-07  
**Status**: Draft  
**Input**: User description: "Enable the MetaTrader.Grpc.Client library to be shared and consumed reliably across projects within the organization, so that any team member can add it to their application and have it work on first run"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Add the client and have it work on first run (Priority: P1)

A developer on another team is starting an application that needs to talk to the
MetaTrader 5 gRPC server. They add the `MetaTrader.Grpc.Client` library to their
project from the organization's shared package source, restore, build, and write
their first call — without cloning this repository, without running any protobuf
code generation by hand, and without hunting down and adding separate gRPC or
protobuf dependencies. It compiles and connects on the first attempt.

**Why this priority**: This is the entire point of the feature. If a first-time
consumer cannot go from "add reference" to "successful call" without manual
plumbing, the library is not reliably shareable. Everything else supports this.

**Independent Test**: On a clean machine with no checkout of this repository,
create a new project, add the published package from the shared source, and run a
minimal call against a reachable server. Success is a compiling, connecting client
with no manual code generation and no manually added transitive dependencies.

**Acceptance Scenarios**:

1. **Given** a clean development machine authorized to access the shared package
   source and no local checkout of this repository, **When** the developer adds
   the `MetaTrader.Grpc.Client` package to a new project and restores, **Then**
   the package and all of its runtime dependencies resolve automatically.
2. **Given** the package has been restored, **When** the developer builds a
   project that references it and writes a minimal client call, **Then** the
   project compiles with no manual protobuf/gRPC code generation step.
3. **Given** a reachable MetaTrader 5 gRPC server, **When** the developer runs the
   minimal call, **Then** the call succeeds (or returns a typed error) using the
   generated clients and wrapper shipped in the package.
4. **Given** a consumer project targeting .NET Framework 4.8 and another targeting
   a modern .NET version, **When** each restores and builds against the package,
   **Then** both succeed without per-framework manual workarounds beyond the
   documented .NET Framework transport prerequisites.

---

### User Story 2 - Know which version is compatible with the running server (Priority: P2)

Before adopting or upgrading the library, a consumer needs to confirm which
package version is right for the MetaTrader 5 server they run against. They can
read the package's version, its proto contract identity, and its tested server
version range directly from the package metadata in the shared source, without
reading source code or asking the maintainers.

**Why this priority**: Reliable consumption across projects means consumers can
self-serve compatibility decisions. Without discoverable compatibility metadata,
teams pick versions by guesswork and file avoidable support requests, but a client
can still technically be added (P1) without it.

**Independent Test**: From the shared package source alone (not the repository),
inspect a published version and confirm its version number, proto contract
identity, tested server version range, usage README, and license are all present
and legible.

**Acceptance Scenarios**:

1. **Given** a published package version in the shared source, **When** a consumer
   inspects its metadata, **Then** the version, proto contract identity, and
   tested server version range are all visible.
2. **Given** a breaking release, **When** a consumer inspects the release notes or
   accompanying documentation, **Then** a migration path from the prior version is
   referenced.
3. **Given** multiple published versions, **When** a consumer lists available
   versions in the shared source, **Then** they can select a specific version and
   see its usage README and license.

---

### User Story 3 - Publish a new version reproducibly (Priority: P3)

A maintainer produces a new release of the library by tagging a version; CI then
builds, tests, packages, and publishes it to the internal feed automatically, so
the feed stays current and every published version is traceable and reproducible
without hands-on publishing.

**Why this priority**: Keeping the feed current is what sustains reliable
consumption over time, but the first consumers can be served by an initial
publish; ongoing publishing cadence is a follow-on concern.

**Independent Test**: Create a release tag on a clean revision and confirm CI
produces and publishes the package to the internal feed, that the published
artifact matches a clean rebuild, and that the version is then retrievable by a
consumer.

**Acceptance Scenarios**:

1. **Given** a clean revision, **When** a maintainer creates the release tag,
   **Then** CI builds, tests, packages, and publishes the version to the internal
   feed with no manual publish step.
2. **Given** a version already published, **When** CI attempts to publish the same
   version number with different content, **Then** the feed rejects it (published
   versions are immutable).
3. **Given** a published version, **When** the same source revision is rebuilt,
   **Then** the produced package is equivalent to the published one.

---

### Edge Cases

- What happens when a developer is not authenticated to (or offline from) the
  shared package source? They MUST get a clear, documented failure rather than a
  partial or silently broken restore.
- What happens when a consumer project already references a different version of a
  shared transitive dependency (e.g. the gRPC or protobuf runtime)? Version
  resolution behavior and the supported dependency range MUST be documented.
- What happens when a consumer adds a package version whose proto contract
  identity does not match the MetaTrader 5 server they run against? The
  compatibility metadata MUST let them detect the mismatch before deploying.
- What happens when a consumer targets a framework outside the supported set? The
  restore/build MUST fail clearly rather than produce a subtly non-working client.
- What happens with pre-release versus stable versions in the shared source? The
  intended-for-production versions MUST be distinguishable from any pre-release
  ones.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The `MetaTrader.Grpc.Client` library MUST be published as a
  self-contained, versioned package to a shared package source that authorized
  members of the organization can consume from other projects.
- **FR-002**: A consumer MUST be able to add a single package reference and, on
  the first restore and build, obtain a working client with no manual protobuf or
  gRPC code generation and no manual editing of generated code.
- **FR-003**: All runtime dependencies required by the library MUST be declared so
  they are resolved automatically when the package is restored; consumers MUST NOT
  need to add supporting dependencies by hand.
- **FR-004**: The package MUST be consumable by projects on every currently
  supported target framework (from .NET Framework 4.8 through modern .NET), with
  any framework-specific transport prerequisites documented rather than requiring
  undocumented workarounds.
- **FR-005**: Published package metadata MUST expose the package version, the proto
  contract identity, and the tested server version range so consumers can confirm
  server compatibility without reading source.
- **FR-006**: The package MUST include usage documentation (README) and license
  information so a consumer can use it without access to this repository.
- **FR-007**: Producing and publishing a package version MUST be reproducible from
  documented repository steps, with no undocumented manual actions.
- **FR-008**: Each published version MUST be immutable — a given version number,
  once published, MUST NOT be replaced with different content.
- **FR-009**: The documentation MUST describe how a consumer connects their
  project to the shared package source, including any required authentication.
- **FR-010**: Package versioning MUST follow the coordinated client SemVer already
  in use; a breaking release MUST reference a migration path from the prior
  version.
- **FR-011**: The distribution MUST make production-intended versions
  distinguishable from any pre-release versions in the shared source.
- **FR-012**: The library MUST be distributed and consumed through an internally
  hosted (on-premises) NuGet feed under the organization's control. Consumers
  connect their project to this feed as an additional package source; the feed's
  authorization model (including any allowance for authenticated or in-network
  anonymous read access) MUST be documented so first-run consumption is
  predictable.
- **FR-013**: Publishing a new version MUST be triggered automatically by
  continuous integration on a version tag/release: creating the release tag causes
  CI to build, test, package, and publish the version to the internal feed, with
  no manual publish step. Publish credentials MUST be held by CI rather than
  distributed to individual maintainers.

### Protocol and MT5 Contract Impact *(mandatory)*

- **Affected RPCs/Messages**: None. This feature is a packaging, distribution, and
  consumption change. It does not add, remove, rename, or renumber any proto
  service, RPC, message, field, or enum, and does not change generated wire
  behavior.
- **Compatibility Decision**: No protocol contract change. This is a
  packaging/release change only. The package continues to carry the existing
  coordinated client version and proto contract identity; distribution changes
  MUST NOT alter the generated contract surface consumers already depend on.
- **MT5 Operation Mapping**: None. No MetaTrader 5 operation, return code, error
  behavior, timestamp handling, or identifier semantics is changed. The library
  continues to expose the same generated operations it exposes today.
- **Cross-Language Type Notes**: None changed. The distributed package exposes the
  same generated types (optional field presence, repeated ordering, timestamps,
  64-bit identifiers, numeric market values) that the current build produces; only
  how the package is delivered and consumed changes.

### Key Entities *(include if feature involves data)*

- **Client Package**: The published, versioned, self-contained artifact of the
  `MetaTrader.Grpc.Client` library. Key attributes: version, proto contract
  identity, tested server version range, usage README, license, declared runtime
  dependencies, supported target frameworks.
- **Shared Package Source**: The organization-accessible location from which
  authorized consumers restore the package. Key attributes: access/authorization
  model, version immutability, version listing, pre-release vs stable distinction.
- **Consumer Project**: An application in another project/team that references the
  package. Key attributes: target framework, existing dependency set, access to
  the shared source.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A team member who has never used the library can go from an empty
  project to a successful (or typed-error) server call in under 15 minutes by
  following the documentation, without cloning this repository.
- **SC-002**: Adding the package resolves 100% of required runtime dependencies
  automatically; the consumer adds zero additional supporting packages by hand.
- **SC-003**: A first build succeeds with no manual protobuf/gRPC code generation
  on both a .NET Framework 4.8 consumer project and a modern .NET consumer
  project.
- **SC-004**: A consumer can determine the compatible server version range and
  proto contract identity entirely from published package metadata, with no source
  access.
- **SC-005**: A maintainer can publish a new version by following the documented
  steps in under 15 minutes, and the published artifact is equivalent to a clean
  rebuild of the same source revision.
- **SC-006**: The package restores and builds successfully on a clean machine with
  no local checkout of this repository (no "works only from the repo" dependency).
- **SC-007**: Attempting to republish an existing version number with different
  content is rejected 100% of the time.

## Assumptions

- The library's source, build, test, and pack steps already work from the
  repository; this feature adds reliable distribution and consumption on top of
  the existing package rather than re-architecting the library itself.
- The current coordinated client version (`0.2.0`), proto contract identity, and
  tested server version range are the values to surface in package metadata; this
  feature does not change them.
- "Organization" means members already authorized to the org's existing developer
  tooling; provisioning brand-new identity/access infrastructure is out of scope.
- Supported target frameworks are those the library already targets via
  `netstandard2.0` (.NET Framework 4.8 through modern .NET); expanding the
  target-framework set is out of scope.
- Documented .NET Framework 4.8 transport prerequisites (TLS and the platform
  HTTP/2 handler) remain the consumer's responsibility and are considered
  documentation, not a defect of distribution.
- The internal NuGet feed already exists or can be stood up as part of this work;
  standing up organization-wide identity infrastructure beyond the feed's own
  access model is out of scope.
- A CI system with the ability to hold publish credentials and react to release
  tags is available for wiring up the automated publish in FR-013.
- Hosting/mirroring the library for consumers outside the organization is out of
  scope.
