#!/usr/bin/env bash
# Per-login launcher for the PREBUILT MT5 gRPC image.
#
# Brings up one isolated container per MT5 login: its own container name, host
# port, and (copy-on-write) writable layer, with NO mounted volume. Repeated
# invocations with distinct --login/--port scale to dozens of containers on one
# host (FR-016, FR-006, FR-007, FR-013, contracts/launcher-cli.md).
#
# Exit codes:
#   0            container started
#   2            missing/invalid arguments
#   3            container name already exists
#   non-zero     Docker failure (e.g. host port in use), surfaced verbatim
set -euo pipefail

DEFAULT_IMAGE="ghcr.io/ins-enco/mt5-grpc-server-prebuilt:latest"

usage() {
    cat <<'EOF'
Usage: run-login.sh --login <LOGIN> --port <HOST_PORT> [options]

Launch one isolated per-login container from the prebuilt MT5 gRPC image.

Required:
  --login <LOGIN>      MT5 account login. Also names the container mt5-grpc-<LOGIN>
                       unless --name is given.
  --port <HOST_PORT>   Host port to publish (integer 1-65535). Published as
                       <BIND>:<HOST_PORT>:50051.

Options:
  --password <PW>      MT5 password (redacted in logs). Default: unset.
  --server <SRV>       MT5 broker server. Default: unset.
  --name <NAME>        Container name. Default: mt5-grpc-<LOGIN>.
  --image <REF>        Prebuilt image reference.
                       Default: ghcr.io/ins-enco/mt5-grpc-server-prebuilt:latest.
  --bind <ADDR>        Host bind address. Default: 127.0.0.1. Pass 0.0.0.0
                       explicitly to expose the endpoint (FR-013).
  --verbose <bool>     Sets GRPC_VERBOSE. Default: true.
  --shm-size <SIZE>    Passed to docker run --shm-size. Default: 1gb.
  -h, --help           Show this help and exit.

Exit codes: 0 started, 2 bad args, 3 name exists, non-zero Docker error.
EOF
}

die() {
    echo "run-login.sh: $1" >&2
    echo "Try 'run-login.sh --help' for usage." >&2
    exit "${2:-2}"
}

login=""
port=""
password=""
server=""
name=""
image="${DEFAULT_IMAGE}"
bind="127.0.0.1"
verbose="true"
shm_size="1gb"

while [ "$#" -gt 0 ]; do
    case "$1" in
        --login)    login="${2:-}"; shift 2 || die "--login requires a value" ;;
        --port)     port="${2:-}"; shift 2 || die "--port requires a value" ;;
        --password) password="${2:-}"; shift 2 || die "--password requires a value" ;;
        --server)   server="${2:-}"; shift 2 || die "--server requires a value" ;;
        --name)     name="${2:-}"; shift 2 || die "--name requires a value" ;;
        --image)    image="${2:-}"; shift 2 || die "--image requires a value" ;;
        --bind)     bind="${2:-}"; shift 2 || die "--bind requires a value" ;;
        --verbose)  verbose="${2:-}"; shift 2 || die "--verbose requires a value" ;;
        --shm-size) shm_size="${2:-}"; shift 2 || die "--shm-size requires a value" ;;
        -h|--help)  usage; exit 0 ;;
        *)          die "unknown argument: $1" ;;
    esac
done

# Validation (launcher-cli.md behavior contract 1).
[ -n "${login}" ] || die "--login is required"
[ -n "${port}" ] || die "--port is required"
case "${port}" in
    ''|*[!0-9]*) die "--port must be a positive integer, got: ${port}" ;;
esac
if [ "${port}" -lt 1 ] || [ "${port}" -gt 65535 ]; then
    die "--port must be in range 1-65535, got: ${port}"
fi

[ -n "${name}" ] || name="mt5-grpc-${login}"

# Name uniqueness (behavior contract 2): refuse rather than clobber.
if docker ps -a --format '{{.Names}}' | grep -qx "${name}"; then
    die "container named '${name}' already exists; remove it or pass --name" 3
fi

# Build the docker run command. NO volume is mounted (behavior contract 4);
# per-login state stays in the container's writable layer.
set -- \
    docker run -d \
    --name "${name}" \
    --restart unless-stopped \
    --shm-size "${shm_size}" \
    -p "${bind}:${port}:50051" \
    -e "MT5_LOGIN=${login}" \
    -e "GRPC_HOST=0.0.0.0" \
    -e "GRPC_PORT=50051" \
    -e "GRPC_VERBOSE=${verbose}"
[ -n "${password}" ] && set -- "$@" -e "MT5_PASSWORD=${password}"
[ -n "${server}" ] && set -- "$@" -e "MT5_SERVER=${server}"
set -- "$@" "${image}"

echo "Launching '${name}' -> ${bind}:${port}:50051 (login ${login})"
# Surface Docker's port-in-use / other errors verbatim (behavior contract 3):
# do not mask the exit status.
exec "$@"
