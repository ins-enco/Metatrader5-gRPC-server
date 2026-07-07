# Contract: Package Distribution & Consumption

**Feature**: `004-client-package-distribution` | **Date**: 2026-07-07
**Input**: [../spec.md](../spec.md), [../research.md](../research.md), [../data-model.md](../data-model.md)

This feature exposes no proto/RPC contract change. Its externally visible
contracts are (1) the **package metadata** a consumer inspects, (2) the **feed
source configuration** a consumer applies, and (3) the **release trigger** a
maintainer uses. Each is specified below with the observable guarantee.

---

## Contract A — Package metadata (what a consumer sees in the feed)

**Producer**: `MetaTrader.Grpc.Client.csproj` pack. **Consumer**: any authorized
member inspecting the package in GitHub Packages.

Required, feed-visible fields on every published version:

| Field | Guarantee | Verifies |
|---|---|---|
| `id` = `MetaTrader.Grpc.Client` | Stable package id | FR-001 |
| `version` | Valid SemVer; pre-release carries a suffix | FR-005, FR-011 |
| README (rendered) | Contains usage docs + current proto contract identity + tested server range | FR-005, FR-006, SC-004 |
| release notes | Contains proto contract identity, tested server range, and (for breaking versions) a `MIGRATION.md` reference | FR-005, FR-010 |
| license | `MIT` (SPDX expression) | FR-006 |
| dependencies | Exactly the runtime deps below; no `Grpc.Tools` | FR-003, SC-002 |
| target framework | `netstandard2.0` | FR-004, SC-003 |

Runtime dependency set (must resolve automatically, none added by hand):

```
Google.Protobuf                          3.29.3
Grpc.Core.Api                            2.71.0
Grpc.Net.Client                          2.71.0
Microsoft.Extensions.Logging.Abstractions 9.0.0
```

`Grpc.Tools` is `PrivateAssets="all"` and MUST NOT appear in the consumer's
dependency graph.

**Guarantee**: A consumer can determine version, proto contract identity, and
tested server range from these fields alone, with no repository checkout
(SC-004, SC-006).

---

## Contract B — Consumer feed source configuration

**Producer**: quickstart docs. **Consumer**: a developer wiring a new project.

A consumer adds the source and authenticates with a GitHub PAT (`read:packages`).
`nuget.config` in the consumer's solution/project directory:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="github-<OWNER>"
         value="https://nuget.pkg.github.com/<OWNER>/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <github-_x003C_OWNER_x003E_>
      <add key="Username" value="%GITHUB_ACTOR%" />
      <add key="ClearTextPassword" value="%GITHUB_PACKAGES_TOKEN%" />
    </github-_x003C_OWNER_x003E_>
  </packageSourceCredentials>
</configuration>
```

> `<OWNER>` is the GitHub organization/user hosting this repository. The source
> key must be a valid XML element name; the example above uses environment
> substitution so no token is committed. `GITHUB_PACKAGES_TOKEN` is a PAT with at
> least `read:packages`.

Consumer then adds the single reference:

```xml
<PackageReference Include="MetaTrader.Grpc.Client" Version="0.2.0" />
```

**Guarantees / failure contract**:
- With a valid token: restore resolves the package and all runtime deps (FR-002,
  FR-003, SC-002); build succeeds with no code-generation step (SC-003).
- Without a valid `read:packages` token, or offline: restore fails with a clear
  authentication/connectivity error — never a partial or silently broken restore
  (spec edge case, FR-009).
- Consumer on an unsupported target framework: restore/build fails clearly rather
  than producing a subtly non-working client (spec edge case).
- .NET Framework 4.8 consumers additionally satisfy the documented TLS +
  `WinHttpHandler` transport prerequisite (FR-004).

---

## Contract C — Release trigger & publish

**Producer**: maintainer creating a tag. **Consumer**: CI publish workflow.

| Aspect | Contract |
|---|---|
| Trigger | Push a tag `csharp-client-v<X.Y.Z>` (pre-release: `csharp-client-v<X.Y.Z>-<label>`) |
| Precondition | Tag version equals csproj `<Version>`; otherwise the job fails before push |
| Actions | restore → build `-c Release` → test → `check-generated.ps1` → pack `-p:ContinuousIntegrationBuild=true` → push |
| Credential | `GITHUB_TOKEN` with `permissions: packages: write`; held by CI only (FR-013) |
| Duplicate version | Push without `--skip-duplicate`; feed returns 409 and the job fails (FR-008, SC-007) |
| Reproducibility | Deterministic + `ContinuousIntegrationBuild` → clean rebuild of the tagged revision is equivalent to the published `.nupkg` (FR-007, SC-005) |

**Guarantees**:
- No manual publish step; every published version is traceable to one tagged
  revision (FR-013, US3 Independent Test).
- Re-publishing an existing version with different content is rejected 100% of the
  time (SC-007).

---

## Verification matrix

| Contract | How verified | Success criteria |
|---|---|---|
| A — metadata | Inspect a packed `.nupkg`'s nuspec + rendered README; assert required fields and dependency set present, `Grpc.Tools` absent | FR-005, FR-003, SC-004 |
| A — metadata drift | Pack-time check: README + release notes contain current `ProtoContractIdentity` and `TestedServerVersionRange` | FR-005 (Decision 4) |
| B — consume | Clean-machine consumer restore/build on net48 and modern .NET from the feed (no repo checkout) | SC-001, SC-002, SC-003, SC-006 |
| B — failure | Restore with no/invalid token and while offline → clear documented error | FR-009, edge case |
| C — publish | Tag a clean revision → CI publishes; consumer can then retrieve it | FR-013, US3 |
| C — immutability | Re-run publish for an existing version → 409, job fails | FR-008, SC-007 |
| C — reproducible | Clean rebuild of the tagged revision equals the published artifact | FR-007, SC-005 |
