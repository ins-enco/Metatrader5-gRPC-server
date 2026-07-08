#!/usr/bin/env bash
# T010 [US1] Single-container startup topology test.
#
# Starts ONE prebuilt container with no volume and asserts:
#   1. gRPC is ready in < 60s              (SC-001, US1 scenario 1)
#   2. logs contain NO install/download lines for Python/VC++/pip/MT5
#                                          (FR-002, US1 scenario 1)
#   3. docker inspect shows zero volume mounts
#                                          (FR-003 / SC-003, US1 scenario 2)
#
# No live broker required: readiness is proven by the server accepting a TCP
# connection on the published port and logging its "Starting gRPC server" line.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
IMAGE_TAG="${IMAGE_TAG:-mt5-grpc-server-prebuilt:test}"
NAME="mt5-prebuilt-smoke-$$"
HOST_PORT="${HOST_PORT:-50151}"
READY_TIMEOUT="${READY_TIMEOUT:-60}"

cleanup() { docker rm -f "${NAME}" >/dev/null 2>&1 || true; }
trap cleanup EXIT

echo "==> Starting single container ${NAME} on 127.0.0.1:${HOST_PORT} (no volume)"
docker run -d --name "${NAME}" \
    --shm-size 1gb \
    -p "127.0.0.1:${HOST_PORT}:50051" \
    -e MT5_LOGIN=100200300 -e MT5_PASSWORD=secret -e MT5_SERVER=Broker-Demo \
    -e GRPC_HOST=0.0.0.0 -e GRPC_PORT=50051 -e GRPC_VERBOSE=true \
    "${IMAGE_TAG}" >/dev/null

echo "==> Waiting up to ${READY_TIMEOUT}s for gRPC readiness"
ready=0
for _ in $(seq 1 "${READY_TIMEOUT}"); do
    if docker logs "${NAME}" 2>&1 | grep -q "Starting gRPC server"; then
        if (exec 3<>"/dev/tcp/127.0.0.1/${HOST_PORT}") 2>/dev/null; then
            ready=1
            break
        fi
    fi
    sleep 1
done
if [ "${ready}" -ne 1 ]; then
    echo "FAIL: gRPC not ready within ${READY_TIMEOUT}s (SC-001)" >&2
    docker logs "${NAME}" >&2 || true
    exit 1
fi
echo "PASS: gRPC ready in time (SC-001)"

echo "==> Asserting no install/download lines in logs (FR-002)"
INSTALL_PATTERN='Installing Microsoft VC\+\+|python-installer|pip install|MetaTrader 5 is missing|Downloading installer|winetricks|vcrun2022|mt5setup'
if docker logs "${NAME}" 2>&1 | grep -Eiq "${INSTALL_PATTERN}"; then
    echo "FAIL: runtime install/download lines found in logs (FR-002)" >&2
    docker logs "${NAME}" 2>&1 | grep -Ei "${INSTALL_PATTERN}" >&2 || true
    exit 1
fi
echo "PASS: no runtime install/download lines (FR-002)"

echo "==> Asserting zero volume mounts (FR-003 / SC-003)"
MOUNT_COUNT="$(docker inspect -f '{{ len .Mounts }}' "${NAME}")"
if [ "${MOUNT_COUNT}" != "0" ]; then
    echo "FAIL: expected 0 mounts, found ${MOUNT_COUNT} (FR-003 / SC-003)" >&2
    docker inspect -f '{{ json .Mounts }}' "${NAME}" >&2 || true
    exit 1
fi
echo "PASS: zero volume mounts (FR-003 / SC-003)"

echo "PASS: single-container startup test"
