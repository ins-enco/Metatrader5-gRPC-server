# Phase 1 Data Model: Shared C# Client Package Distribution

**Feature**: `004-client-package-distribution` | **Date**: 2026-07-07
**Input**: [spec.md](./spec.md), [research.md](./research.md)

This feature ships no runtime data model and no proto message. The "entities"
below are the packaging/distribution artifacts and their attributes — the things a
consumer, maintainer, and CI act on. Each maps to concrete fields in the csproj,
`nuget.config`, and the publish workflow.

---

## Entity: Client Package

The published, versioned, self-contained `.nupkg` for `MetaTrader.Grpc.Client`.

| Attribute | Source of truth | Feed-visible? | Requirement |
|---|---|---|---|
| Package id | `<PackageId>MetaTrader.Grpc.Client` | Yes | FR-001 |
| Version | `<Version>` (SemVer; pre-release suffix for pre-release) | Yes | FR-005, FR-010, FR-011 |
| Proto contract identity | `<ProtoContractIdentity>` → quoted in README + `<PackageReleaseNotes>` | Yes (README/notes) | FR-005, SC-004 |
| Tested server version range | `<TestedServerVersionRange>` → quoted in README + `<PackageReleaseNotes>` | Yes (README/notes) | FR-005, SC-004 |
| Usage README | `<PackageReadmeFile>README.md` (packed) | Yes | FR-006, SC-001 |
| License | `<PackageLicenseExpression>MIT` | Yes | FR-006 |
| Declared runtime dependencies | `<PackageReference>` (Google.Protobuf, Grpc.Core.Api, Grpc.Net.Client, Microsoft.Extensions.Logging.Abstractions) | Yes (deps list) | FR-003, SC-002 |
| Build-only tool (excluded) | `Grpc.Tools` `PrivateAssets="all"` | No (not a consumer dep) | FR-002, FR-003 |
| Supported target framework | `<TargetFramework>netstandard2.0` | Yes | FR-004, SC-003 |
| Migration reference (breaking) | `<PackageReleaseNotes>` → `MIGRATION.md` | Yes | FR-010 |

**Validation rules**
- Version is valid SemVer; a pre-release version carries a pre-release suffix,
  a production version does not (FR-011).
- README and release notes MUST contain the current `ProtoContractIdentity` and
  `TestedServerVersionRange` string values (drift guard, Decision 4).
- The consumer runtime graph MUST NOT require `Grpc.Tools` or any package the
  consumer must add by hand (FR-003, SC-002).

**State / lifecycle**
`authored (repo)` → `built & tested (CI on tag)` → `packed` → `published
(immutable in feed)`. Once `published`, the (id, version) pair is immutable: a
second publish of the same version with different content is rejected (FR-008,
SC-007).

---

## Entity: Shared Package Source (GitHub Packages)

The organization-controlled GitHub Packages NuGet registry from which authorized
consumers restore.

| Attribute | Value / mechanism | Requirement |
|---|---|---|
| Source URL | `https://nuget.pkg.github.com/<OWNER>/index.json` | FR-001, FR-012 |
| Authorization model | GitHub token with `read:packages` (consume) / `write:packages` (publish); scoped to `<OWNER>` | FR-009, FR-012 |
| Version immutability | Native: re-publishing an existing version returns HTTP 409 | FR-008, SC-007 |
| Version listing | Feed lists all published versions of the package id | FR-005 |
| Pre-release vs stable | Distinguished by SemVer pre-release suffix; clients exclude pre-release by default | FR-011 |

**Validation rules**
- A restore without a valid `read:packages` token, or while offline, MUST fail
  with a clear authentication/connectivity error (documented), never a silent or
  partial restore (spec edge case).

---

## Entity: Consumer Project

An application in another team/project that references the package.

| Attribute | Value / mechanism | Requirement |
|---|---|---|
| Target framework | Any supported: .NET Framework 4.8 … modern .NET (resolves `netstandard2.0`) | FR-004, SC-003 |
| Package source config | `nuget.config` adds the GitHub Packages source + env-substituted PAT | FR-009, SC-001 |
| Existing dependency set | May already reference Google.Protobuf / gRPC runtime; supported range documented | edge case (dependency conflict) |
| net48 transport prerequisite | TLS + `WinHttpHandler` (documented, consumer responsibility) | FR-004 |

**Validation rules**
- Adding the single package reference and restoring resolves 100% of runtime
  dependencies with zero hand-added packages (SC-002).
- A first build succeeds with no manual protobuf/gRPC code generation on both a
  net48 and a modern-.NET consumer (SC-003).
- A consumer targeting a framework outside the supported set MUST fail restore/
  build clearly rather than produce a subtly non-working client (edge case).

---

## Entity: Release / Publish Pipeline

The CI process that turns a tagged revision into a published, traceable package.

| Attribute | Value / mechanism | Requirement |
|---|---|---|
| Trigger | Git tag `csharp-client-v<X.Y.Z>[-<prerelease>]` | FR-013 |
| Steps | restore → build (Release) → test → `check-generated.ps1` → pack (`ContinuousIntegrationBuild=true`) → version-consistency assert → push | FR-007, FR-013 |
| Version-consistency guard | Tag version MUST equal csproj `<Version>`; mismatch fails the job | US3 |
| Publish credential | Built-in `GITHUB_TOKEN` with `packages: write`; not distributed to maintainers | FR-013 |
| Duplicate handling | `dotnet nuget push` without `--skip-duplicate` → 409 on re-publish | FR-008, SC-007 |
| Reproducibility | Deterministic + `ContinuousIntegrationBuild`; clean rebuild of the tagged revision is equivalent to the published artifact | FR-007, SC-005 |

**State / lifecycle**
`tag pushed` → `job runs on tagged revision` → (all gates pass) → `pushed to feed`
| (version already exists) → `rejected (409)` | (tag ≠ csproj version) → `failed
before push`.

---

## Cross-entity invariants

- **No contract change**: the distributed package exposes the same generated types
  and wire behavior as today; distribution changes MUST NOT alter the contract
  surface consumers depend on (spec *Protocol and MT5 Contract Impact*).
- **Single authored version**: `<Version>` in the csproj is the one place the
  package version is set; the tag and the feed derive from it (no divergent
  numbers).
- **Compatibility self-service**: version + proto contract identity + tested
  server range are all determinable from feed-visible fields alone, with no
  repository checkout (FR-005, SC-004, SC-006).
