#!/usr/bin/env bash
# T014 [US2] Launcher argument/validation tests (launcher-cli.md exit codes +
# behavior contract 1-2). Pure CLI checks — no image build or container start is
# required for the argument-validation cases.
#
# Asserts:
#   - missing --login             -> exit 2
#   - missing --port              -> exit 2
#   - non-numeric --port          -> exit 2
#   - out-of-range --port         -> exit 2
#   - existing container name     -> exit 3
#   - --help                      -> exit 0 and prints usage
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LAUNCHER="${SCRIPT_DIR}/../run-login.sh"

fail=0
check_exit() {
    local desc="$1"; local expected="$2"; shift 2
    "$@" >/dev/null 2>&1
    local actual="$?"
    if [ "${actual}" -eq "${expected}" ]; then
        echo "PASS: ${desc} (exit ${actual})"
    else
        echo "FAIL: ${desc} — expected exit ${expected}, got ${actual}" >&2
        fail=1
    fi
}

check_exit "missing --login exits 2"       2 bash "${LAUNCHER}" --port 50051
check_exit "missing --port exits 2"        2 bash "${LAUNCHER}" --login 100200300
check_exit "non-numeric --port exits 2"    2 bash "${LAUNCHER}" --login 100200300 --port abc
check_exit "zero --port exits 2"           2 bash "${LAUNCHER}" --login 100200300 --port 0
check_exit "out-of-range --port exits 2"   2 bash "${LAUNCHER}" --login 100200300 --port 70000
check_exit "unknown flag exits 2"          2 bash "${LAUNCHER}" --login 100200300 --port 50051 --bogus x

# --help prints usage and exits 0.
if bash "${LAUNCHER}" --help 2>&1 | grep -q "Usage: run-login.sh"; then
    echo "PASS: --help prints usage"
else
    echo "FAIL: --help did not print usage" >&2
    fail=1
fi
check_exit "--help exits 0" 0 bash "${LAUNCHER}" --help

# Existing container name -> exit 3. Uses a throwaway container so no image is
# needed; skipped gracefully if Docker is unavailable.
if command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then
    NAME="mt5-prebuilt-nameclash-$$"
    docker run -d --name "${NAME}" --entrypoint sleep hello-world infinity >/dev/null 2>&1 \
        || docker create --name "${NAME}" hello-world >/dev/null 2>&1 || true
    if docker ps -a --format '{{.Names}}' | grep -qx "${NAME}"; then
        check_exit "existing container name exits 3" 3 \
            bash "${LAUNCHER}" --login 100200300 --port 50051 --name "${NAME}"
    else
        echo "SKIP: could not create fixture container for name-clash check"
    fi
    docker rm -f "${NAME}" >/dev/null 2>&1 || true
else
    echo "SKIP: Docker unavailable — name-clash (exit 3) check skipped"
fi

if [ "${fail}" -ne 0 ]; then
    echo "FAIL: launcher argument tests" >&2
    exit 1
fi
echo "PASS: launcher argument tests"
