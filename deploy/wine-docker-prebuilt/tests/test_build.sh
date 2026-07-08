#!/usr/bin/env bash
# T009 [US1] Build smoke test for the prebuilt image.
#
# Builds deploy/wine-docker-prebuilt/Dockerfile with default ARGs and asserts:
#   1. the build succeeds, and
#   2. terminal64.exe was baked into the resulting image's /wineprefix
#      (SC-006, build-args.md invariant 1).
#
# No live broker is required. Requires Docker with BuildKit and network access
# to the Python + MT5 installer sources at BUILD time.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../../.." && pwd)"
IMAGE_TAG="${IMAGE_TAG:-mt5-grpc-server-prebuilt:test}"

echo "==> Building prebuilt image ${IMAGE_TAG} (default ARGs)"
docker build \
    -f "${REPO_ROOT}/deploy/wine-docker-prebuilt/Dockerfile" \
    -t "${IMAGE_TAG}" \
    "${REPO_ROOT}"

echo "==> Asserting terminal64.exe exists in the baked image"
TERMINAL_PATH="/wineprefix/drive_c/Program Files/MetaTrader 5/terminal64.exe"
if docker run --rm --entrypoint /usr/bin/test "${IMAGE_TAG}" -f "${TERMINAL_PATH}"; then
    echo "PASS: terminal64.exe is baked into ${IMAGE_TAG}"
else
    echo "FAIL: terminal64.exe missing from ${IMAGE_TAG}:${TERMINAL_PATH}" >&2
    exit 1
fi

echo "PASS: build smoke test"
