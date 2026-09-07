# Specification Quality Checklist: C# Client StreamDeals Surface

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-09-07
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic (no implementation details)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Notes

- **Content Quality, item 1 — domain interpretation.** This feature's deliverable *is* a
  library API, and the constitution's own spec template mandates a "Protocol and MT5
  Contract Impact" section naming affected RPCs, messages, and field numbers. The spec
  therefore names the proto contract, the existing public operations it must preserve, the
  two channel constructors, and the two target frameworks — these are the user-facing
  contract for a package consumer, not implementation detail. Internal structure (invoker
  types, file layout, class design) is deliberately excluded and left to `/speckit-plan`.
  Reviewed and accepted as compliant for a client-library feature.
- **Requirement Completeness, item 1 — resolved (2026-09-07).** The single marker on FR-015
  asked how server versioning and the client's `<ProtoContractIdentity>` /
  `<TestedServerVersionRange>` metadata should be reconciled. Root cause found during spec
  research: both `mt5_grpc_proto/setup.py` and `mt5_grpc_server/setup.py` still declare
  `0.3.0` although `StreamDeals` landed after `0.3.0` shipped, so the tested range
  `[0.3.0,1.0.0)` would advertise support from servers that do not implement the RPC.
  **Decision: Option A** — release the Python packages as `0.4.0`, declare
  `[0.4.0,1.0.0)`, and move the contract identity to `protos-007-stream-deals`. This is a
  machine-checkable gate rather than prose a human must read, and it satisfies the
  constitution's requirement that package and contract versioning be coordinated.
  Recorded as FR-015, FR-016, SC-009, SC-010, and an expanded Compatibility Decision.
- **Accepted scope widening.** Option A pulls a release action (not a code change) outside
  `mt5_grpc_client_csharp`: `bump_version.py minor` for the two Python packages plus a root
  `CHANGELOG.md` entry. The feature description scoped the work to the client library, so
  this widening is called out explicitly in the spec's Assumptions with the reason it was
  accepted, and the publish-ordering dependency is recorded so client work can be completed
  and verified even if the two releases cannot ship together.
- **Identity naming judgment.** The existing convention is `protos-<spec-dir-name>`
  (`protos-005-trade-transaction-events`). `protos-007-stream-deals` follows the shape but
  drops the `csharp` qualifier from this spec's directory name, because the value marks a
  language-neutral proto contract state, not the consuming client feature. The drift-guard
  test only requires the authored value to appear verbatim in the README and release notes,
  so this is a readability decision, and the plan phase may revisit it.
- **Assumptions to confirm, not blockers.** Public surface names, the convenience surface's
  discard-partials-on-failure behaviour, and the absence of any client-side chunk-size
  policy are all recorded as explicit assumptions in the spec so they can be overridden at
  spec time rather than discovered after publication.
- **New finding surfaced by this spec.** The server's chunk-size cap of 1000 deals is
  roughly 77 KB at the observed ~77 bytes/deal, which sits inside the band between the
  largest observed safe response (~75 KB) and the smallest observed fatal one (~121 KB).
  A caller who requests the maximum chunk size may therefore re-enter the failure regime
  this feature exists to escape. Captured as an edge case, folded into FR-005 and FR-011,
  and flagged in Assumptions as evidence-derived rather than measured.

**Status: 16 of 16 items pass. Spec is ready for `/speckit-plan`.**
