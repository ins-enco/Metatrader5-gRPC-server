#!/usr/bin/env bash
# T011 [US1] Offline-start test (US1 scenario 3).
#
# Runs the prebuilt container with outbound access to installer sources blocked
# and confirms it STILL starts and serves gRPC. Because Python, VC++, the
# packages, and MT5 are baked at build time, the container must not need any
# network access to installer sources at runtime (FR-002, FR-009).
#
# Network isolation uses `--network none`, which removes all outbound access
# (a strict superset of "installer sources blocked"). Readiness is proven by the
# in-container server logging its "Starting gRPC server" line (no published port
# is reachable with --network none, so we assert on the log marker).
set -euo pipefail

IMAGE_TAG="${IMAGE_TAG:-mt5-grpc-server-prebuilt:test}"
NAME="mt5-prebuilt-offline-$$"
READY_TIMEOUT="${READY_TIMEOUT:-60}"

cleanup() { docker rm -f "${NAME}" >/dev/null 2>&1 || true; }
trap cleanup EXIT

echo "==> Starting container ${NAME} with outbound network disabled (--network none)"
docker run -d --name "${NAME}" \
    --network none \
    --shm-size 1gb \
    -e MT5_LOGIN=100200300 -e MT5_PASSWORD=secret -e MT5_SERVER=Broker-Demo \
    -e GRPC_HOST=0.0.0.0 -e GRPC_PORT=50051 -e GRPC_VERBOSE=true \
    "${IMAGE_TAG}" >/dev/null

echo "==> Waiting up to ${READY_TIMEOUT}s for the server to start offline"
ready=0
for _ in $(seq 1 "${READY_TIMEOUT}"); do
    if docker logs "${NAME}" 2>&1 | grep -q "Starting gRPC server"; then
        ready=1
        break
    fi
    if [ "$(docker inspect -f '{{ .State.Running }}' "${NAME}" 2>/dev/null)" = "false" ]; then
        echo "FAIL: container exited before serving while offline" >&2
        docker logs "${NAME}" >&2 || true
        exit 1
    fi
    sleep 1
done

if [ "${ready}" -ne 1 ]; then
    echo "FAIL: server did not start within ${READY_TIMEOUT}s while offline (US1 scenario 3)" >&2
    docker logs "${NAME}" >&2 || true
    exit 1
fi

echo "PASS: container starts and serves with installer sources unreachable (US1 scenario 3)"
