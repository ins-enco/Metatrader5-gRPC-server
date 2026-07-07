# Implementation Plan: Shared C# Client Package Distribution

**Branch**: `004-client-package-distribution` | **Date**: 2026-07-07 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/004-client-package-distribution/spec.md`

## Summary

Make the existing `MetaTrader.Grpc.Client` (`netstandard2.0`) library reliably
consumable from other projects by publishing it as a versioned, self-contained
package to an organization-controlled shared source and wiring reproducible,
tag-triggered publishing. There is **no proto/contract, generated-binding, server,
or wire/MT5 behavior change** — only how the package is delivered and consumed.

Concretely: publish to the **GitHub Packages NuGet registry**
(`https://nuget.pkg.github.com/<OWNER>/index.json`); a new **client-scoped
tag-triggered** GitHub Actions workflow (`csharp-client-v<X.Y.Z>`) builds, tests,
packs deterministically, and pushes with the built-in `GITHUB_TOKEN` (credentials
never distributed); consumers add the source with a `read:packages` token and get
a working client on first restore/build with zero hand-added dependencies and no
code generation; and compatibility metadata (version, proto contract identity,
tested server range) is surfaced through feed-visible fields (version, packed
README, release notes) with a drift guard.

**Spec-alignment note**: FR-012 (and related phrasing in FR-001/FR-013/Assumptions)
says "internally hosted (on-premises) NuGet feed." Per explicit planning
direction, this plan uses **GitHub Packages** — organization-controlled,
authorization-gated, immutable, added as an extra source with documented auth —
which satisfies FR-012's substance but not the literal "on-premises" constraint.
Read those requirements as "organization-controlled shared package source (GitHub
Packages)". See [research.md](./research.md) Decision 1; a spec amendment can
follow.

## Technical Context

**Language/Version**: C# client `netstandard2.0` (unchanged); no proto/Python change
**Primary Dependencies**: Google.Protobuf 3.29.3, Grpc.Core.Api/Grpc.Net.Client 2.71.0, Microsoft.Extensions.Logging.Abstractions 9.0.0 (runtime); Grpc.Tools 2.71.0 (`PrivateAssets="all"`, build-only)
**Storage**: N/A
**Testing**: xUnit (existing Tests / ContractTests / CompatibilityTests); plus a clean-consumer restore/build verification on net48 + modern .NET; a metadata/dependency assertion on the packed `.nupkg`
**Target Platform**: `netstandard2.0` package consumed by .NET Framework 4.8 → modern .NET
**Project Type**: Packaging/distribution change in a multi-language repo; scope is `mt5_grpc_client_csharp/` + `.github/workflows/` + docs
**Performance Goals**: N/A (no runtime change); pack preserves protobuf binary surface
**Constraints**: No contract/wire change; version immutability; reproducible pack (Deterministic + ContinuousIntegrationBuild); publish credentials held by CI only; consumer first-run under 15 min
**Scale/Scope**: One package, one new publish workflow, csproj/metadata touch-ups, docs (README/quickstart/nuget.config); no proto files touched

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Protocol contract**: **PASS**. No `.proto` file, RPC, message, field number,
  or enum is added, removed, renamed, or renumbered; generated bindings and the
  wire surface are unchanged. The distributed package carries the existing
  coordinated version and proto contract identity, and distribution changes MUST
  NOT alter the contract surface consumers depend on. See spec *Protocol and MT5
  Contract Impact* and [research.md](./research.md) (intro).
- **MT5 behavior fidelity**: **PASS**. No MetaTrader 5 operation, return code,
  error behavior, timestamp handling, or identifier semantic changes; the library
  exposes the same generated operations it exposes today (spec *MT5 Operation
  Mapping: None*).
- **Multi-language type safety**: **PASS**. The package ships the same generated
  types (optional presence, repeated ordering, timestamps, 64-bit identifiers,
  numeric market values); nothing becomes language-specific. Only delivery
  changes. See [data-model.md](./data-model.md) *Cross-entity invariants*.
- **Reliability and observability**: **PASS**. The relevant failure modes are
  distribution-level and covered: unauthenticated/offline restore yields a clear,
  documented failure rather than a partial/broken restore; unsupported target
  framework fails clearly; publish credentials are held only by CI
  (`GITHUB_TOKEN`), never distributed, and never logged. See
  [contracts/package-distribution.md](./contracts/package-distribution.md)
  Contracts B and C.
- **Test and release reproducibility**: **PASS** — this is the core of the
  feature. Verification runs from repository steps: `dotnet build/test`,
  `check-generated.ps1`, deterministic `dotnet pack` with
  `ContinuousIntegrationBuild=true`, a packed-`.nupkg` metadata/dependency
  assertion, a clean-consumer restore/build on net48 and modern .NET, and
  feed-level immutability (409 on re-publish). Commands are listed in
  [quickstart.md](./quickstart.md) and the verification matrix in
  [contracts/package-distribution.md](./contracts/package-distribution.md).

No violations. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/004-client-package-distribution/
├── plan.md              # This file
├── research.md          # Phase 0 output (6 distribution decisions)
├── data-model.md        # Phase 1 output (package/source/consumer/pipeline entities)
├── quickstart.md        # Phase 1 output (consumer + maintainer flows)
├── contracts/
│   └── package-distribution.md   # metadata / feed-config / release-trigger contracts
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
protos/                  # UNCHANGED (no contract change)
mt5_grpc_proto/          # UNCHANGED
mt5_grpc_server/         # UNCHANGED

mt5_grpc_client_csharp/
├── src/MetaTrader.Grpc.Client/
│   └── MetaTrader.Grpc.Client.csproj   # VERIFY/ADJUST: metadata surfaced in feed-visible
│                                       #   fields (README + PackageReleaseNotes carry
│                                       #   ProtoContractIdentity + TestedServerVersionRange);
│                                       #   confirm runtime deps + Grpc.Tools PrivateAssets=all
├── Directory.Build.props               # Deterministic already true (CI adds ContinuousIntegrationBuild)
├── README.md                           # EXTEND: install-from-GitHub-Packages + nuget.config + auth
├── scripts/
│   ├── check-generated.ps1             # REUSE (drift gate in publish workflow)
│   └── check-package-metadata.ps1      # NEW: assert packed .nupkg metadata + dep set (Contract A)
└── examples/nuget.config               # NEW (sample): consumer feed source config

.github/workflows/
└── csharp-client-publish.yml           # NEW: tag csharp-client-v* → build/test/pack/push to
                                        #   GitHub Packages via GITHUB_TOKEN (immutable, reproducible)
# (existing csharp-client.yml CI on push/PR stays as the build/test gate)
```

**Structure Decision**: The library already builds/tests/packs (existing
`csharp-client.yml` + `check-generated.ps1`); this feature adds the *distribution*
layer around it, so changes are confined to `mt5_grpc_client_csharp/` (metadata
surfacing, docs, a metadata-assertion script, a sample `nuget.config`) and a new
`.github/workflows/csharp-client-publish.yml`. Publishing is a **separate**
tag-triggered workflow (not the push/PR CI) and uses a **client-scoped** tag so
it stays independent of the server's `v*.*.*` Docker release in `docker-ghcr.yml`.
No proto/server code is touched, consistent with the "distribution only" scope.

## Complexity Tracking

> No Constitution Check violations. The one notable deviation is the distribution
> channel (GitHub Packages rather than a literal on-premises feed), which is a
> documented refinement of FR-012 made under explicit planning direction, not a
> constitution violation. This section is intentionally empty.
