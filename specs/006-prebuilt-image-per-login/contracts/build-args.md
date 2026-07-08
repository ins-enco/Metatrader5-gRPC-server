# Contract: Prebuilt image build arguments

**File**: `deploy/wine-docker-prebuilt/Dockerfile`

Reproducible build inputs (FR-009, FR-010, SC-006, edge case "build-time source
unavailable").

## Build ARGs

| ARG | Default | Meaning / Validation |
| --- | --- | --- |
| `PYTHON_VERSION` | `3.11.9` | Windows Python installed into the baked prefix. Must be a valid python.org release; the `wget` fails the build otherwise. |
| `NUMPY_SPEC` | `numpy<2` | NumPy pip requirement baked at build. Default avoids Wine/UCRT issues. |
| `MT5_SETUP_URL` | MetaQuotes CDN URL | Source of the MT5 installer at build time. Override to a pinned/internal mirror for strict reproducibility. Unreachable URL → build fails. |

## Base image

- `FROM ubuntu:24.04` — same base as the bootstrap image, for parity.

## Build-time invariants (MUST hold or the build fails)

1. After the MT5 install step, `/wineprefix/drive_c/Program Files/MetaTrader
   5/terminal64.exe` MUST exist; otherwise the build exits non-zero (no
   incomplete image is produced).
2. The Python interpreter, VC++ runtime (`vcrun2022`), and the
   `mt5_grpc_proto` + `mt5_grpc_server` packages MUST all be installed into the
   baked `/wineprefix` at build time — none installed at runtime (FR-002).
3. No `VOLUME` instruction for `/wineprefix` (keeps the zero-volume contract,
   FR-003 / SC-003).
4. The build MUST NOT change or depend on the bootstrap Dockerfile (FR-008).

## Reproducibility note

Rebuilding from the same `PYTHON_VERSION`, `NUMPY_SPEC`, and a pinned
`MT5_SETUP_URL` yields a functionally equivalent image (SC-006). Because the
default MetaQuotes CDN URL is not version-pinned, operators requiring strict
byte/version reproducibility SHOULD supply an internal pinned installer mirror
via `MT5_SETUP_URL`. This responsibility is documented in the quickstart.
