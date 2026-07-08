#!/usr/bin/env bash
# T016 [US2] Host port-collision test ("host port collision" edge case,
# launcher-cli.md behavior contract 3).
#
# Launches a container on a host port, then launches a SECOND container on the
# same host port and asserts:
#   - the second launch fails with a non-zero exit, and
#   - the first container keeps its endpoint (the launcher never silently takes
#     over another login's endpoint).
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LAUNCHER="${SCRIPT_DIR}/../run-login.sh"
IMAGE_TAG="${IMAGE_TAG:-mt5-grpc-server-prebuilt:test}"
HOST_PORT="${HOST_PORT:-50161}"
NAME_A="mt5-prebuilt-collide-a-$$"
NAME_B="mt5-prebuilt-collide-b-$$"

cleanup() {
    docker rm -f "${NAME_A}" "${NAME_B}" >/dev/null 2>&1 || true
}
trap cleanup EXIT

echo "==> Launching first container ${NAME_A} on port ${HOST_PORT}"
bash "${LAUNCHER}" --login 100200300 --port "${HOST_PORT}" --name "${NAME_A}" \
    --image "${IMAGE_TAG}" >/dev/null
if [ "$(docker inspect -f '{{ .State.Running }}' "${NAME_A}" 2>/dev/null)" != "true" ]; then
    echo "FAIL: first container did not start" >&2
    docker logs "${NAME_A}" >&2 || true
    exit 1
fi

echo "==> Launching second container ${NAME_B} on the SAME port ${HOST_PORT} (must fail)"
bash "${LAUNCHER}" --login 100200301 --port "${HOST_PORT}" --name "${NAME_B}" \
    --image "${IMAGE_TAG}" >/dev/null 2>&1
second_exit="$?"
if [ "${second_exit}" -eq 0 ]; then
    echo "FAIL: second launch on an in-use port unexpectedly succeeded" >&2
    exit 1
fi
echo "PASS: second launch failed with non-zero exit ${second_exit}"

echo "==> Asserting first container still owns the endpoint"
if [ "$(docker inspect -f '{{ .State.Running }}' "${NAME_A}" 2>/dev/null)" != "true" ]; then
    echo "FAIL: first container was disrupted by the collision attempt" >&2
    exit 1
fi
echo "PASS: first container endpoint intact (behavior contract 3)"

echo "PASS: port-collision test"
