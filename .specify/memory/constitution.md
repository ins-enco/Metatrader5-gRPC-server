<!--
Sync Impact Report
Version change: template -> 1.0.0
Modified principles:
- Template Principle 1 -> I. Protocol Contracts Are the Product
- Template Principle 2 -> II. MT5 Behavior Fidelity and Trading Safety
- Template Principle 3 -> III. Type-Safe Multi-Language Interoperability
- Template Principle 4 -> IV. Reliability, Performance, and Observability
- Template Principle 5 -> V. Testable, Reproducible Change
Added sections:
- Project Constraints
- Development Workflow and Quality Gates
Removed sections:
- Placeholder Section 2
- Placeholder Section 3
Templates requiring updates:
- OK updated: .specify/templates/plan-template.md
- OK updated: .specify/templates/spec-template.md
- OK updated: .specify/templates/tasks-template.md
- OK reviewed, not present: .specify/templates/commands/*.md
- OK reviewed, no update required: README.md
- OK reviewed, no update required: mt5_grpc_server/README.md
- OK reviewed, no update required: mt5_grpc_proto/README.md
Follow-up TODOs: None
-->
# Metatrader5 gRPC Server Constitution

## Core Principles

### I. Protocol Contracts Are the Product
Protocol Buffer definitions under `protos/` are the canonical public contract for
all server and client behavior. Every externally visible RPC, request, response,
enum value, field type, field number, and error shape MUST be defined there
before implementation work depends on it. Proto field numbers MUST NOT be
reused, renamed incompatibly, or removed without a documented compatibility
plan and a major version decision. Generated language bindings MUST be
regenerated from the proto sources for contract changes; generated code MUST
NOT be edited as the source of truth.

Rationale: the project exists to provide reliable, type-safe, multi-language
communication through strongly defined gRPC contracts.

### II. MT5 Behavior Fidelity and Trading Safety
Server behavior MUST preserve the observable semantics of the corresponding
MetaTrader 5 operation, including required inputs, optional values, return codes,
timestamps, symbol/order identifiers, and terminal error reporting. Trading
operations MUST validate inputs before forwarding them to MetaTrader 5 when the
contract exposes enough information to do so, and MUST return structured error
information instead of silently dropping, coercing, or masking failures.

Rationale: incorrect trading or market-data behavior can cause financial loss,
so convenience wrappers cannot override platform semantics.

### III. Type-Safe Multi-Language Interoperability
Features MUST remain usable by clients in any gRPC-supported language. Public
contracts MUST avoid Python-only assumptions, implementation-specific class
names, ambiguous scalar encodings, and undocumented sentinel values. Time,
money-like numeric values, identifiers, optional fields, and repeated values
MUST use protobuf types and comments that make cross-language behavior explicit.

Rationale: the server and proto packages are a language-neutral integration
surface, not only a Python client library.

### IV. Reliability, Performance, and Observability
The server MUST favor bounded, observable gRPC behavior suitable for trading and
market-data workloads. New RPCs and streaming flows MUST define failure modes,
timeouts or cancellation behavior where applicable, and logging that supports
diagnosing request, response, and MetaTrader 5 failures without exposing secrets
or account credentials. Implementations MUST avoid unnecessary blocking,
unbounded memory growth, and per-request work that is avoidable through the
existing gRPC and protobuf APIs.

Rationale: callers rely on this bridge for remote automation where latency,
failure visibility, and resource control are part of correctness.

### V. Testable, Reproducible Change
Behavioral changes MUST include focused tests at the lowest practical level.
Contract changes and new RPC behavior MUST include generated-code verification
and at least one contract or integration test path that can run without a live
broker account by using mocks, fakes, or documented test doubles. Packaging and
release changes MUST be reproducible from repository scripts and documented
commands.

Rationale: the project spans proto contracts, generated code, server adapters,
and packages; changes are only safe when the full path can be rebuilt and
verified.

## Project Constraints

The repository contains two primary Python packages: `mt5_grpc_proto` for
protobuf and generated gRPC bindings, and `mt5_grpc_server` for the MetaTrader 5
server implementation. Source proto files live in `protos/`; generated Python
bindings live in `mt5_grpc_proto/mt5_grpc_proto/`.

Python package support MUST remain compatible with the declared package metadata
unless a release plan explicitly changes it. Server features that require the
MetaTrader 5 Python package MUST account for its Windows-first runtime and the
documented Wine workflow. Remote deployments SHOULD use TLS when crossing host
or trust boundaries, and documentation MUST make insecure defaults explicit.

Versioning of published packages and protobuf contracts MUST be coordinated.
Backward-compatible additions use minor or patch releases according to impact;
breaking public contract changes require a major release or an explicit
migration strategy.

## Development Workflow and Quality Gates

Feature work MUST start from a specification that states user value, affected
RPCs or proto messages, MT5 operations involved, expected error behavior, and
measurable success criteria. Plans MUST pass the Constitution Check before Phase
0 research and again after design.

Before implementation, contract-impacting work MUST update proto definitions and
record compatibility decisions. During implementation, changes MUST keep server
adapters aligned with generated protobuf types. Before completion, contributors
MUST run the relevant generation, unit, contract, or integration checks and
document any checks that could not be run.

Reviews MUST verify protocol compatibility, MT5 semantic fidelity, security of
remote access and logging, performance risks for streaming or high-frequency
calls, and packaging/version consistency.

## Governance

This constitution supersedes informal development preferences when planning or
reviewing features in this repository. Amendments require a documented change to
this file, a Sync Impact Report, and updates to affected Spec Kit templates or
runtime guidance.

Versioning follows semantic versioning for governance. MAJOR changes remove or
redefine principles or compatibility obligations. MINOR changes add principles,
sections, or materially expanded requirements. PATCH changes clarify wording or
fix non-semantic errors.

Every feature plan MUST include a Constitution Check. Non-compliance is allowed
only when the plan documents the violation, the reason it is necessary, the
simpler alternative that was rejected, and the mitigation or migration path.

**Version**: 1.0.0 | **Ratified**: 2026-04-28 | **Last Amended**: 2026-04-28
