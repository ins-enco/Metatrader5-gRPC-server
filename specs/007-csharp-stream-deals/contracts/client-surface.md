# Contract: C# Client Deal-History Surface

**Feature**: [../spec.md](../spec.md) | **Plan**: [../plan.md](../plan.md) | **Date**: 2026-09-07

The wire contract is unchanged — see [deal.proto](./deal.proto), a verbatim snapshot of
`protos/deal.proto` included for reference only. The contract this feature *authors* is the
public C# surface of `MetaTrader.Grpc.Client` `5.1.0`, specified below. It is a published
NuGet package, so everything here is a compatibility commitment.

---

## A. Preserved surface (no change permitted)

```csharp
public Task<Mt5GrpcResult<DealsResponse>> GetDealsAsync(
    DealsRequest? request = null,
    DateTime? deadline = null,
    CancellationToken cancellationToken = default);
```

- Signature, semantics, operation label (`"TradeHistoryService.GetDeals"`) and error
  behaviour are **byte-for-byte as shipped in `5.0.2`** (FR-006).
- It MUST NOT fall back to `StreamDeals` under any condition.
- Its XML documentation gains a note on the large-history failure mode and a pointer to
  `StreamDealsAsync` (FR-011). Documentation-only; behaviour is untouched.
- SC-005: every existing caller — the two examples, all four test projects, the benchmarks
  — compiles and passes unmodified.

## B. `StreamDealsAsync` — chunk-level surface (P1)

```csharp
public IAsyncEnumerable<DealsResponse> StreamDealsAsync(
    DealsRequest? request = null,
    DateTime? deadline = null,
    CancellationToken cancellationToken = default);
```

Routed through the existing `Mt5GrpcStreamingInvoker` with operation label
`"TradeHistoryService.StreamDeals"`, error selector `response => response.Error`, and the
caller's request forwarded as `request ?? new DealsRequest()`.

| ID | Guarantee |
| --- | --- |
| B1 | One yielded item per server message, in server order. Zero items for an empty filtered history, completing normally with no error (FR-001, FR-003). |
| B2 | The caller's `DealsRequest` is transmitted verbatim: `chunk_size` unset stays unset (`HasChunkSize == false` on the wire), a set value is transmitted unchanged including values above the server cap. The client sets, raises, lowers, clamps, defaults and clears nothing (FR-005). |
| B3 | An in-band error message (`error.code != 0`) terminates enumeration by throwing `Mt5GrpcClientException` whose `.Error` carries the mapped `Mt5ErrorCode` / message. No item is yielded from that message (FR-002). |
| B4 | A transport fault terminates enumeration the same way, including `Unimplemented` from a pre-`0.4.0` server. **No fallback to `GetDealsAsync`** (FR-002, FR-006). |
| B5 | Cancelling `cancellationToken` or exceeding `deadline` terminates enumeration promptly with the mapped cancellation / deadline error and releases the call (FR-008, SC-004: control returns within 1 s). |
| B6 | Abandoning enumeration early (`break` out of `await foreach`) releases the call — enumerator disposal disposes the `AsyncServerStreamingCall` (FR-008). |
| B7 | Retention is one chunk at a time; the library accumulates nothing (SC-007). |
| B8 | Concurrent streams on one client are independent: one faulting, cancelling or being abandoned does not disturb another. |
| B9 | Logging is per call and per failure only, never per deal or per chunk, and carries no request payload, credentials or account identifiers (FR-010). |

## C. `GetAllDealsAsync` — convenience surface (P3)

```csharp
public Task<Mt5GrpcResult<DealsResponse>> GetAllDealsAsync(
    DealsRequest? request = null,
    DateTime? deadline = null,
    CancellationToken cancellationToken = default);
```

Consumes `StreamDealsAsync` internally and returns the library's standard result type — the
same type `GetDealsAsync` returns, so migration is a rename.

| ID | Guarantee |
| --- | --- |
| C1 | Success: `IsSuccess == true`, `Error == null`, and `Value.Deals` is every chunk's deals appended in stream order. `Value` is a client-synthesized `DealsResponse` whose `Error` is always absent (FR-004). |
| C2 | Empty filtered history: `IsSuccess == true` with `Value.Deals.Count == 0` (FR-003). |
| C3 | Failure (in-band error, transport fault, cancellation, deadline, unexpected exception): `IsSuccess == false`, `Error` carries the mapped error, `Value` is `null`. **No partial collection is returned, and the method does not throw** (FR-004). |
| C4 | Chunk-size handling is identical to B2 — pass-through only. |
| C5 | Whole-history client memory is the documented trade-off; the XML documentation states the cost and recommends `StreamDealsAsync` for large histories (FR-011, US3-AC4). |

## D. Parity contract (FR-007, SC-002)

For identical filters against the same history, `GetAllDealsAsync` and the concatenation of
`StreamDealsAsync` chunks MUST equal `GetDealsAsync` in **deal count, order and ticket
sequence**. Verified across the full matrix:

| Filter form | `group` unset | `group` set |
| --- | --- | --- |
| `time_filter` | required | required |
| `ticket` | required | required |
| `position` | required | required |

## E. Reachability contract (FR-009, SC-006)

| ID | Guarantee |
| --- | --- |
| E1 | Both methods are present in the `netstandard2.0` **and** `net472` package outputs. |
| E2 | Both work through `Mt5GrpcClientFactory.Create` (modern `GrpcChannel`) and `Mt5GrpcClientFactory.CreateCore` (native `Grpc.Core` channel, .NET Framework 4.8 / Windows 10) with identical observable results, including prompt cancellation. The native path is required, not optional. |
| E3 | A `net472` / `net48` consumer and a `netstandard2.0` consumer each reference the package and call both surfaces plus `DealsRequest.ChunkSize` with **no additional package reference** — `Microsoft.Bcl.AsyncInterfaces` already ships as a dependency of every target. |

## F. Generated-surface contract (FR-012)

| ID | Guarantee |
| --- | --- |
| F1 | `Metatrader.V1.TradeHistoryService.TradeHistoryServiceClient` exposes `StreamDeals`. |
| F2 | `Metatrader.V1.DealsRequest` exposes the chunk-size field with proto3 presence (`ChunkSize`, `HasChunkSize`, `ClearChunkSize`) on field number 5. |
| F3 | `ProtoContractCatalog.StreamingServices` gains `["TradeHistoryService"] = ["StreamDeals"]`. `UnaryServices` is **unchanged**, keeping `TradeHistoryService` → `GetDeals` and the asserted counts of 16 services / 31 unary RPCs intact. |

## G. Package metadata contract (FR-014..FR-016, SC-009, SC-010)

| Property | From | To |
| --- | --- | --- |
| `<Version>` | `5.0.2` | `5.1.0` |
| `<ProtoContractIdentity>` | `protos-005-trade-transaction-events` | `protos-007-stream-deals` |
| `<TestedServerVersionRange>` | `[0.3.0,1.0.0)` | `[0.4.0,1.0.0)` |
| `mt5_grpc_proto` / `mt5_grpc_server` | `0.3.0` | `0.4.0` |

| ID | Guarantee |
| --- | --- |
| G1 | `protos-007-stream-deals` and `[0.4.0,1.0.0)` appear **verbatim** in the packed `README.md` and in `<PackageReleaseNotes>`, satisfying both existing guards (`DocumentationAccuracyTests` and `check-package-metadata.ps1`) with no waiver. |
| G2 | The per-target dependency set is unchanged — no package is added, removed or re-versioned, so `check-package-metadata.ps1`'s exact-set assertion stays green. |
| G3 | No `0.3.0` server is described as supporting the streaming surface anywhere in the package's documentation or metadata. |
| G4 | The client `5.1.0` publish is gated on a `0.4.0` `mt5_grpc_proto` / `mt5_grpc_server` release existing. Client work may be completed and verified beforehand; only the publish step is blocked (FR-016). |

## H. Documentation contract (FR-011)

The README section and both runnable examples MUST each state or demonstrate:

1. Backfill once through `StreamDealsAsync`, then fetch incrementally with
   `TimeFilter.date_from` set to the last held deal's timestamp.
2. The unbounded client memory cost of `GetAllDealsAsync` and the recommendation to prefer
   `StreamDealsAsync` for large histories.
3. The server's chunk-size default (500) and cap (1000), that the client transmits the
   caller's value verbatim, and that a chunk size at or near the cap (~77 KB) re-enters the
   band where server aborts were observed.
4. The large-history failure mode of `GetDealsAsync`, with a pointer to `StreamDealsAsync`.
5. That a pre-`0.4.0` server fails the call as unimplemented, with no automatic fallback.

The `net48` example demonstrates all of this over `Mt5GrpcClientFactory.CreateCore`, which
also serves as the compile-time proof for E1/E2 on the `net472` asset.

---

## Non-goals (explicit)

- No proto change, no generated-code edit, no server behaviour change.
- No client-side chunk-size default, cap or validation.
- No automatic fallback in either direction between the two RPCs.
- No partial results alongside an error.
- No new NuGet dependency, no new target framework, no new public type.
- Server resilience to an oversized single write remains a server concern, out of scope.
