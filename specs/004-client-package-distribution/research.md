# Phase 0 Research: Shared C# Client Package Distribution

**Feature**: `004-client-package-distribution` | **Date**: 2026-07-07
**Input**: [spec.md](./spec.md)

This feature is a packaging, distribution, and consumption change only. It does
not touch `protos/`, generated bindings, the server, or any wire/MT5 behavior
(see spec *Protocol and MT5 Contract Impact*). Research therefore centers on the
distribution channel, reproducible publishing, and discoverable compatibility
metadata — not on the library's runtime surface.

---

## Decision 1 — Distribution channel: GitHub Packages NuGet registry

**Decision**: Publish and consume `MetaTrader.Grpc.Client` through the GitHub
Packages NuGet registry at `https://nuget.pkg.github.com/<OWNER>/index.json`,
scoped to the organization/owner that already hosts this repository. This is the
"shared package source" of FR-001/FR-012.

**Rationale**:
- Organization-controlled and authorization-gated: only members with a token
  carrying `read:packages` for the owner can restore, satisfying "authorized
  members of the organization can consume from other projects" (FR-001, FR-012).
- The repository and its container images already live in the same GitHub owner
  (`docker-ghcr.yml` publishes to `ghcr.io/${owner}`), so the client package
  lands beside the existing artifacts under one access model — no new identity
  infrastructure (spec Assumption: provisioning new identity/access is out of
  scope).
- GitHub-hosted Actions runners reach `nuget.pkg.github.com` natively, so the
  publish job needs no self-hosted runner and no network exposure work.
- Version immutability is native: GitHub Packages rejects re-publishing an
  existing version (HTTP 409), directly satisfying FR-008 and SC-007.

**Spec-alignment note (refines FR-012)**: FR-012 as written says "internally
hosted (on-premises) NuGet feed." The chosen channel, GitHub Packages, is
cloud-hosted, not on-premises. Per explicit direction during planning, GitHub
Packages is adopted because it satisfies the *substance* of FR-012 —
organization-controlled, authorization-gated, immutable, added by consumers as an
extra package source with documented auth — while dropping the literal
"on-premises" constraint. The spec's FR-012 wording (and the related
"internal/on-premises feed" phrasing in FR-001, FR-013, and the Assumptions)
should be read as "organization-controlled shared package source (GitHub
Packages)". This is recorded here rather than blocking; a spec amendment can
follow.

**Alternatives considered**:
- *BaGetter / self-hosted NuGet server* — true on-prem, but requires standing up
  and operating a service plus reaching it from CI; rejected in favor of the
  already-present GitHub owner infrastructure.
- *Sonatype Nexus / JFrog Artifactory / ProGet / Azure DevOps Server* —
  enterprise feeds with immutability and auth, but assume infrastructure the repo
  does not already use; rejected as heavier than needed.
- *Public nuget.org* — rejected: the audience is intra-organization and
  authorization-gated, not the public.

---

## Decision 2 — Consumer authentication and first-run source configuration

**Decision**: Consumers add the GitHub Packages source with a NuGet package
source that authenticates using a GitHub Personal Access Token (classic) carrying
at minimum `read:packages`, supplied via `nuget.config` (`packageSourceCredentials`
with `%GITHUB_PACKAGES_TOKEN%` environment substitution) rather than a checked-in
secret. The quickstart documents the exact `nuget.config` and the environment
variable, and documents the failure a consumer sees when unauthenticated or
offline.

**Rationale**:
- GitHub Packages requires authentication for NuGet restore even for
  public-repository packages, so "add reference and it just works" (US1) depends
  on the consumer first wiring the authenticated source — this is the one
  documented prerequisite, satisfying FR-009 and the "not authenticated / offline"
  edge case (a clear, documented failure, not a silent partial restore).
- Environment-substituted credentials keep tokens out of source control and match
  how CI supplies its own token, so the same `nuget.config` shape works locally
  and in CI.

**Alternatives considered**:
- *`dotnet nuget add source` with `--username`/`--password` inline* — works but
  pushes people toward pasting tokens on the command line / into shell history;
  documented as an alternative, not the primary path.
- *GITHUB_TOKEN for consumers* — only exists inside Actions; not available to a
  developer on a workstation, so a PAT is required for the local first-run flow.

---

## Decision 3 — Reproducible, immutable publishing from CI on a release tag

**Decision**: A dedicated GitHub Actions workflow publishes the package to GitHub
Packages, triggered by a **client-scoped** git tag `csharp-client-v<X.Y.Z>`
(optionally with a pre-release suffix, e.g. `csharp-client-v0.3.0-preview.1`).
The job: checks out the tagged revision, restores, builds `-c Release`, runs the
full test suite, runs `check-generated.ps1`, packs, asserts the packed version
equals both the csproj `<Version>` and the tag's version, then pushes with
`dotnet nuget push` authenticated by the built-in `GITHUB_TOKEN`
(`permissions: packages: write`). The push does **not** use `--skip-duplicate`, so
an attempt to republish an existing version fails visibly (409).

**Rationale**:
- Tag-triggered, no manual publish step, credentials held by CI (`GITHUB_TOKEN`),
  never distributed to maintainers — satisfies FR-007 and FR-013.
- A **client-scoped** tag prefix decouples the client release from the server's
  existing `v*.*.*` Docker release (`docker-ghcr.yml`), so bumping the server
  image does not publish the client and vice versa; the client keeps its own
  coordinated SemVer (`0.2.0` today) per FR-010 and spec Assumptions.
- Asserting tag-version == csproj-version prevents publishing a package whose
  number disagrees with the tag, keeping every published version traceable to one
  revision (US3 Independent Test).
- Not skipping duplicates makes SC-007 observable: republishing an existing
  version is rejected by the feed rather than silently succeeding.
- Determinism: `Directory.Build.props` already sets `<Deterministic>true</Deterministic>`.
  The workflow additionally sets `ContinuousIntegrationBuild=true` (via
  `-p:ContinuousIntegrationBuild=true`) during pack so the produced `.nupkg` is a
  function of source only, making a clean rebuild of the same revision equivalent
  to the published artifact (FR-007, SC-005).

**Alternatives considered**:
- *Trigger on GitHub Releases instead of tags* — equivalent, but a tag is the
  lowest-friction "cut a version" action and matches the repo's existing
  tag-driven Docker publish; releases can be layered on later.
- *Reuse the server `v*.*.*` tag* — rejected: couples two independently versioned
  artifacts and would publish the client on every server bump.
- *`--skip-duplicate`* — rejected: hides the immutability rejection that SC-007
  requires to be observable.

---

## Decision 4 — Surfacing compatibility metadata in feed-visible package fields

**Decision**: Expose the package version, proto contract identity, and tested
server version range through **standard, feed-visible nuspec fields** — the
`<Version>`, the packed `README.md` (already `PackageReadmeFile`), and
`<PackageReleaseNotes>` — with the custom MSBuild properties
`<ProtoContractIdentity>` and `<TestedServerVersionRange>` remaining the single
authored source that those human-readable fields quote. A build check fails the
pack if the release notes / README no longer contain the current
`ProtoContractIdentity` and `TestedServerVersionRange` values, preventing drift.

**Rationale**:
- Custom MSBuild properties (`ProtoContractIdentity`, `TestedServerVersionRange`)
  are **not** written into the `.nuspec` inside the produced `.nupkg`, so a
  consumer inspecting the package in the feed would not see them. FR-005/SC-004
  require these to be visible from package metadata *without source access*, so
  they must live in fields the feed renders: version, README, and release notes.
- The current package already carries the contract identity and server range in
  both `PackageReleaseNotes` and the packed `README.md`; this decision formalizes
  that as the contract and adds a drift guard so the human-readable copy cannot
  fall out of sync with the authored properties.

**Alternatives considered**:
- *Custom `<PackageProperties>` / arbitrary nuspec metadata* — NuGet has no
  general-purpose custom metadata field that feeds render for consumers; rejected.
- *Separate sidecar metadata file only* — a consumer inspecting the feed listing
  would still not see it inline; the README (which the feed renders) is the
  discoverable surface, so metadata is kept there.

---

## Decision 5 — Pre-release vs. stable distinction

**Decision**: Production-intended versions use plain SemVer (`0.2.0`); any
pre-release uses a SemVer pre-release suffix (`0.3.0-preview.1`). Consumers select
stable-only by not opting into pre-release in their tooling; the tag scheme in
Decision 3 carries the same suffix.

**Rationale**: SemVer pre-release labels are the native NuGet mechanism for
distinguishing production from pre-release versions, and NuGet clients exclude
pre-release by default — directly satisfying FR-011 and the pre-release edge case
with no bespoke convention.

**Alternatives considered**: *Separate feed for pre-release* — unnecessary
overhead; the SemVer suffix already segregates them within one source.

---

## Decision 6 — Target frameworks and dependency completeness (no change)

**Decision**: Keep the single `netstandard2.0` target; do not expand the
target-framework set. Confirm all runtime dependencies (`Google.Protobuf`,
`Grpc.Core.Api`, `Grpc.Net.Client`, `Microsoft.Extensions.Logging.Abstractions`)
are declared `PackageReference`s that flow transitively, and that `Grpc.Tools`
stays `PrivateAssets="all"` (a build-time code generator, correctly excluded from
consumers' runtime graph). Document the .NET Framework 4.8 transport
prerequisites (TLS + `WinHttpHandler`) as consumer responsibilities.

**Rationale**:
- `netstandard2.0` already spans .NET Framework 4.8 through modern .NET
  (FR-004, spec Assumptions), so no target change is needed and none is in scope.
- FR-003/SC-002 require zero hand-added supporting packages; the four runtime
  dependencies are ordinary transitive `PackageReference`s, and `Grpc.Tools`
  running code generation inside the *package build* means consumers get the
  already-generated clients with no code-generation step of their own (FR-002,
  SC-003). This must be verified by a consumer-side restore/build, not assumed.
- The net48 TLS/`WinHttpHandler` requirement is documentation, not a distribution
  defect (spec Assumptions), so it belongs in the quickstart's net48 note.

**Alternatives considered**: *Multi-targeting (add `net8.0`/`net48`)* — rejected:
out of scope per Assumptions and unnecessary because `netstandard2.0` already
resolves for the supported consumers.

---

## Summary of resolved unknowns

| Unknown | Resolution |
|---|---|
| Which shared package source? | GitHub Packages NuGet registry (Decision 1; refines FR-012) |
| How consumers authenticate first-run | PAT `read:packages` via `nuget.config` env substitution (Decision 2) |
| How publishing is triggered/kept immutable | Client-scoped tag → Actions → `GITHUB_TOKEN` push, no `--skip-duplicate` (Decision 3) |
| How compatibility metadata is discoverable | Version + packed README + release notes, drift-guarded (Decision 4) |
| Pre-release vs stable | SemVer pre-release suffix (Decision 5) |
| Target frameworks / dependency completeness | Unchanged `netstandard2.0`; verify transitive deps, keep Grpc.Tools private (Decision 6) |

No NEEDS CLARIFICATION items remain.
