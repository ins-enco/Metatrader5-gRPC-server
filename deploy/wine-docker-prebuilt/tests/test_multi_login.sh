#!/usr/bin/env bash
# T015 [US2] Multi-container isolation test.
#
# Launches TWO logins on TWO ports via the launcher and asserts:
#   1. each container serves independently on its own port (SC-002)
#   2. `docker rm -f` of one leaves the other still serving (SC-004)
#   3. neither container has any volume mount (FR-003 / FR-004)
#
# No live broker required: readiness is proven by each server logging its
# "Starting gRPC server" line and accepting a TCP connection on its port.
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LAUNCHER="${SCRIPT_DIR}/../run-login.sh"
IMAGE_TAG="${IMAGE_TAG:-mt5-grpc-server-prebuilt:test}"
PORT_A="${PORT_A:-50171}"
PORT_B="${PORT_B:-50172}"
NAME_A="mt5-prebuilt-iso-a-$$"
NAME_B="mt5-prebuilt-iso-b-$$"
READY_TIMEOUT="${READY_TIMEOUT:-60}"

cleanup() {
    docker rm -f "${NAME_A}" "${NAME_B}" >/dev/null 2>&1 || true
}
trap cleanup EXIT

wait_ready() {
    local name="$1"; local port="$2"
    for _ in $(seq 1 "${READY_TIMEOUT}"); do
        if docker logs "${name}" 2>&1 | grep -q "Starting gRPC server" \
            && (exec 3<>"/dev/tcp/127.0.0.1/${port}") 2>/dev/null; then
            return 0
        fi
        sleep 1
    done
    return 1
}

assert_no_mounts() {
    local name="$1"
    local count
    count="$(docker inspect -f '{{ len .Mounts }}' "${name}")"
    if [ "${count}" != "0" ]; then
        echo "FAIL: ${name} has ${count} mounts, expected 0 (FR-003/FR-004)" >&2
        return 1
    fi
    return 0
}

echo "==> Launching two per-login containers"
bash "${LAUNCHER}" --login 100200300 --port "${PORT_A}" --name "${NAME_A}" \
    --password secretA --server Broker-Demo --image "${IMAGE_TAG}" >/dev/null
bash "${LAUNCHER}" --login 100200301 --port "${PORT_B}" --name "${NAME_B}" \
    --password secretB --server Broker-Demo --image "${IMAGE_TAG}" >/dev/null

echo "==> Waiting for both to serve independently (SC-002)"
if ! wait_ready "${NAME_A}" "${PORT_A}"; then
    echo "FAIL: ${NAME_A} not ready on ${PORT_A}" >&2; docker logs "${NAME_A}" >&2 || true; exit 1
fi
if ! wait_ready "${NAME_B}" "${PORT_B}"; then
    echo "FAIL: ${NAME_B} not ready on ${PORT_B}" >&2; docker logs "${NAME_B}" >&2 || true; exit 1
fi
echo "PASS: both containers serve on distinct ports (SC-002)"

echo "==> Asserting no volume mounts on either container (FR-003/FR-004)"
assert_no_mounts "${NAME_A}" || exit 1
assert_no_mounts "${NAME_B}" || exit 1
echo "PASS: zero volume mounts on both (FR-003/FR-004)"

echo "==> Removing ${NAME_A}; ${NAME_B} must keep serving (SC-004)"
docker rm -f "${NAME_A}" >/dev/null
sleep 2
if [ "$(docker inspect -f '{{ .State.Running }}' "${NAME_B}" 2>/dev/null)" != "true" ] \
    || ! (exec 3<>"/dev/tcp/127.0.0.1/${PORT_B}") 2>/dev/null; then
    echo "FAIL: ${NAME_B} was disrupted by removal of ${NAME_A} (SC-004)" >&2
    exit 1
fi
echo "PASS: surviving container unaffected by peer removal (SC-004)"

echo "PASS: multi-login isolation test"
