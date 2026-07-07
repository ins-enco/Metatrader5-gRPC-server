---

description: "Task list for Shared C# Client Package Distribution"
---

# Tasks: Shared C# Client Package Distribution

**Input**: Design documents from `/specs/004-client-package-distribution/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/package-distribution.md](./contracts/package-distribution.md), [quickstart.md](./quickstart.md)

**Tests**: This is a packaging/distribution change, so verification tasks are
included per the template rule (packaging counts). They are **packaging
verifications** — nuspec metadata assertions, a clean-consumer restore/build, and
documentation drift guards — not runtime/wire tests, because the feature makes **no
proto/contract, generated-binding, server, or MT5 behavior change** (see plan
*Constitution Check* and spec *Protocol and MT5 Contract Impact*).

**Organization**: Tasks are grouped by user story (P1 → P3) so each is
independently implementable and testable.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)

## Path Conventions

Distribution scope is confined to `mt5_grpc_client_csharp/`, `.github/workflows/`,
and this feature's docs. No `protos/`, `mt5_grpc_proto/`, or `mt5_grpc_server/`
files are touched.

- Package project: `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/`
- Scripts: `mt5_grpc_client_csharp/scripts/`
- Tests: `mt5_grpc_client_csharp/tests/`
- Workflows: `.github/workflows/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish a known-good build baseline before distribution work.

- [X] T001 Verify baseline is green: `dotnet restore`, `dotnet build -c Release`, and `dotnet test -c Release` on `mt5_grpc_client_csharp/MetaTrader.Grpc.Client.sln`, and run `mt5_grpc_client_csharp/scripts/check-generated.ps1 -Configuration Release`, so distribution changes start from a passing build/test/drift state.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Package-shape correctness every user story depends on (a package that
is self-contained and deterministically packable).

**⚠️ CRITICAL**: No user story work should begin until this phase is complete.

- [X] T002 [P] Confirm/complete runtime dependency declarations and build-only exclusion in `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/MetaTrader.Grpc.Client.csproj`: all runtime deps (Google.Protobuf, Grpc.Core.Api, Grpc.Net.Client, Microsoft.Extensions.Logging.Abstractions) are `PackageReference`s that flow transitively, and `Grpc.Tools` stays `PrivateAssets="all"` so it never enters the consumer graph (FR-003).
- [X] T003 [P] Confirm README and license are packed in `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/MetaTrader.Grpc.Client.csproj`: `PackageReadmeFile` resolves to a packed `README.md` and `PackageLicenseExpression` is `MIT`, so a consumer gets usage docs + license without repository access (FR-006).
- [X] T004 Confirm deterministic build settings in `mt5_grpc_client_csharp/Directory.Build.props` (`<Deterministic>true</Deterministic>` present) so a clean rebuild is reproducible; note that CI applies `ContinuousIntegrationBuild=true` at pack time (FR-007).

**Checkpoint**: The package is self-contained and deterministically packable — user stories can begin.

---

## Phase 3: User Story 1 - Add the client and have it work on first run (Priority: P1) 🎯 MVP

**Goal**: A consumer with no checkout of this repo adds the package from the shared
source, restores, builds, and makes a call — with zero hand-added dependencies and
no code-generation step, on both .NET Framework 4.8 and modern .NET.

**Independent Test**: On a clean environment, add the packed package from a source,
restore, build a net48 and a modern-.NET consumer, and run a minimal call
(success or typed error) — no manual protobuf/gRPC codegen, no manually added deps.

### Tests for User Story 1 ⚠️

> Write these verification scripts first and confirm they FAIL against the current
> state before completing the implementation tasks.

- [X] T005 [P] [US1] Create `mt5_grpc_client_csharp/scripts/check-package-metadata.ps1` that packs the project and asserts the produced `.nupkg` nuspec declares exactly the runtime dependencies, contains **no** `Grpc.Tools` dependency, targets `netstandard2.0`, and includes the packed `README.md` (SC-002; Contract A).
- [X] T006 [P] [US1] Create `mt5_grpc_client_csharp/scripts/verify-consumer-restore.ps1` that publishes the packed `.nupkg` to a temporary local folder source, generates throwaway consumer projects targeting `net48` and a modern `net*` TFM that reference `MetaTrader.Grpc.Client`, and asserts each restores and builds with no protobuf/gRPC code-generation step and no hand-added packages (SC-002, SC-003, SC-006).

### Implementation for User Story 1

- [X] T007 [US1] Create the sample consumer source config `mt5_grpc_client_csharp/examples/nuget.config` adding `https://nuget.pkg.github.com/<OWNER>/index.json` with env-substituted credentials (`%GITHUB_ACTOR%` / `%GITHUB_PACKAGES_TOKEN%`), no token committed (FR-009; Contract B).
- [X] T008 [US1] Extend `mt5_grpc_client_csharp/README.md` with an "Install from GitHub Packages" section: the `nuget.config` source, the required `read:packages` token, the single `PackageReference`, and the documented unauthenticated/offline restore failure (FR-009, SC-001; edge cases).
- [X] T009 [US1] Align `specs/004-client-package-distribution/quickstart.md` consumer flow with the final `nuget.config`/README wording (owner placeholder, token env vars, net48 TLS + `WinHttpHandler` note) so the under-15-minute path is accurate (SC-001, FR-004).

**Checkpoint**: A clean consumer can add, restore, build, and call the package on net48 and modern .NET — MVP complete.

---

## Phase 4: User Story 2 - Know which version is compatible with the running server (Priority: P2)

**Goal**: A consumer confirms the package version, proto contract identity, and
tested server version range from feed-visible metadata alone, without source.

**Independent Test**: Inspect a packed version's feed-visible fields and confirm
version, proto contract identity, tested server range, README, and license are all
present and legible; a breaking version references a migration path.

### Tests for User Story 2 ⚠️

- [X] T010 [P] [US2] Add a documentation drift-guard test in `mt5_grpc_client_csharp/tests/MetaTrader.Grpc.Client.ContractTests/DocumentationAccuracyTests.cs` asserting the packed/authored `README.md` **and** `<PackageReleaseNotes>` both contain the current `ProtoContractIdentity` and `TestedServerVersionRange` values (FR-005, Decision 4).
- [X] T011 [P] [US2] Extend `mt5_grpc_client_csharp/scripts/check-package-metadata.ps1` to also assert the nuspec `version`, `licenseExpression`, `releaseNotes`, and packed `README` carry the compatibility metadata (proto contract identity + tested server range) a consumer needs without source access (SC-004; Contract A).

### Implementation for User Story 2

- [X] T012 [US2] Ensure `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/MetaTrader.Grpc.Client.csproj` keeps `ProtoContractIdentity`/`TestedServerVersionRange` as the authored source and mirrors them into `<PackageReleaseNotes>`, with the README quoting the same values; a breaking version's release notes reference `MIGRATION.md` (FR-005, FR-010).
- [X] T013 [US2] Document the pre-release vs stable distinction (SemVer pre-release suffix; NuGet excludes pre-release by default) in `mt5_grpc_client_csharp/README.md` and `specs/004-client-package-distribution/quickstart.md` (FR-011).

**Checkpoint**: Compatibility is fully self-service from feed metadata; US1 still works.

---

## Phase 5: User Story 3 - Publish a new version reproducibly (Priority: P3)

**Goal**: Creating a client-scoped release tag makes CI build, test, pack
deterministically, and publish an immutable version to GitHub Packages with no
manual publish step and credentials held only by CI.

**Independent Test**: Tag a clean revision, confirm CI publishes and a consumer can
then retrieve the version; a clean rebuild equals the published artifact;
re-publishing an existing version is rejected (409).

### Tests for User Story 3 ⚠️

- [X] T014 [P] [US3] Add a version-consistency guard (a step in the publish workflow or a `mt5_grpc_client_csharp/scripts/check-tag-version.ps1`) that fails before push if the `csharp-client-v<X.Y.Z>` tag version does not equal `<Version>` in `mt5_grpc_client_csharp/src/MetaTrader.Grpc.Client/MetaTrader.Grpc.Client.csproj` (US3 Independent Test).
- [X] T015 [P] [US3] Document the immutability and reproducibility verification (re-publish of an existing version returns 409 and fails the job; clean rebuild of the tagged revision is equivalent to the published `.nupkg`) in `specs/004-client-package-distribution/quickstart.md` maintainer section (FR-008, SC-005, SC-007).

### Implementation for User Story 3

- [X] T016 [US3] Create `.github/workflows/csharp-client-publish.yml` triggered by tags `csharp-client-v*` (with `permissions: packages: write`): checkout tagged revision → restore → build `-c Release` → test → `check-generated.ps1` → `check-package-metadata.ps1` → tag/version guard (T014) → `dotnet pack -p:ContinuousIntegrationBuild=true` → `dotnet nuget push` to `https://nuget.pkg.github.com/<OWNER>/index.json` using `GITHUB_TOKEN`, **without** `--skip-duplicate` (FR-007, FR-008, FR-013, SC-007).
- [X] T017 [US3] Document the maintainer publish flow (bump `<Version>`, verify locally, tag `csharp-client-v<X.Y.Z>`, push) in `mt5_grpc_client_csharp/README.md` and confirm `specs/004-client-package-distribution/quickstart.md` matches, emphasizing credentials live only in CI (FR-013).

**Checkpoint**: All three stories are independently functional; publishing is reproducible and immutable.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Consistency and end-to-end validation across stories.

- [X] T018 [P] Add a distribution entry to `mt5_grpc_client_csharp/CHANGELOG.md` describing GitHub Packages consumption and tag-triggered publishing (no version/contract change).
- [X] T019 [P] Add a short "Consuming the C# client package" pointer in the root `README.md` linking to the client README's install section.
- [X] T020 Run the full `specs/004-client-package-distribution/quickstart.md` end-to-end (consumer restore/build via T006 script + maintainer dry-run pack) and record any doc corrections (SC-001, SC-005, SC-006).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories.
- **User Stories (Phase 3–5)**: All depend on Foundational. US1 → US2 → US3 by
  priority; US2 and US3 are independently testable but naturally build on the
  packaged, metadata-correct artifact from earlier stories.
- **Polish (Phase 6)**: Depends on the desired user stories being complete.

### User Story Dependencies

- **US1 (P1)**: After Foundational. No dependency on other stories. **MVP.**
- **US2 (P2)**: After Foundational. Reuses the metadata script from US1 (T011
  extends T005) but is independently testable.
- **US3 (P3)**: After Foundational. The publish workflow invokes the US1/US2
  verification scripts (T016 calls T005/T011/T014) but the publishing behavior is
  independently testable via a tag.

### Within Each User Story

- Verification scripts/tests (marked ⚠️) written and failing before implementation.
- Config/csproj before docs; workflow after its guard scripts exist.

### Parallel Opportunities

- T002, T003 (Foundational) run in parallel — different concerns in the csproj/props.
- T005, T006 (US1 tests) run in parallel — separate script files.
- T010, T011 (US2 tests) run in parallel — different files (test vs script).
- T014, T015 (US3 tests) run in parallel — script vs docs.
- T018, T019 (Polish) run in parallel — different files.

---

## Parallel Example: User Story 1

```bash
# US1 verification scripts (write first, expect FAIL):
Task: "Create scripts/check-package-metadata.ps1 asserting nuspec deps + no Grpc.Tools + README (T005)"
Task: "Create scripts/verify-consumer-restore.ps1 building net48 + modern consumer from local nupkg (T006)"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 1 Setup → 2. Phase 2 Foundational → 3. Phase 3 US1.
4. **STOP and VALIDATE**: run T005 + T006 — a clean consumer restores, builds, and
   calls on net48 and modern .NET with zero hand-added deps and no codegen.
5. This is the shippable core (a consumable, self-contained package).

### Incremental Delivery

1. Setup + Foundational → package is self-contained and deterministically packable.
2. US1 → clean consumer works first-run → **MVP**.
3. US2 → compatibility is self-service from feed metadata.
4. US3 → tag-triggered, reproducible, immutable publishing.
5. Polish → docs consistency + end-to-end quickstart validation.

---

## Notes

- [P] tasks = different files, no dependencies.
- [Story] label maps each task to its user story for traceability.
- No proto/server/generated-binding changes — verification is packaging-level.
- Replace `<OWNER>` with the GitHub organization/user hosting this repository.
- Commit after each task or logical group; stop at any checkpoint to validate a story.
