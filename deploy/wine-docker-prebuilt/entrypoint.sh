#!/usr/bin/env bash
set -euo pipefail

# Runtime-only entrypoint for the PREBUILT image.
#
# Python, the VC++ runtime, the server packages, and the MetaTrader 5 terminal
# are already baked into /wineprefix at build time (see the Dockerfile). This
# script therefore performs NO install or download at runtime (FR-002,
# contracts/container-env.md). It only: starts Xvfb, writes the per-login
# autostart INI, launches the terminal, and starts the gRPC server. Logging and
# secret redaction are preserved because it invokes the same server module with
# the same GRPC_VERBOSE default as the bootstrap image (FR-011).

display="${DISPLAY:-:99}"
screen="${XVFB_SCREEN:-1024x768x16}"

Xvfb "${display}" -screen 0 "${screen}" >/tmp/xvfb.log 2>&1 &
xvfb_pid="$!"

cleanup() {
    kill "${xvfb_pid}" >/dev/null 2>&1 || true
}
trap cleanup EXIT

sleep 2

if [ "$#" -gt 0 ]; then
    exec "$@"
fi

python_exe='C:\Python311\python.exe'
mt5_terminal_path="${MT5_TERMINAL_PATH:-C:\Program Files\MetaTrader 5\terminal64.exe}"

# The Wine prefix is baked at build time, but each container is a fresh
# environment: bring up wineserver/services and re-assert the Windows version
# before launching the terminal. Without this, the baked terminal64.exe crashes
# with "wine: Unhandled illegal instruction" (the bootstrap image runs the same
# init every start — see deploy/wine-docker/entrypoint.sh).
wineboot --init
wine reg add "HKEY_CURRENT_USER\\Software\\Wine" /v Version /t REG_SZ /d "win10" /f
wineserver -w

# Generate an MT5 startup config so the headless terminal enables AutoTrading
# automatically (there is no GUI to click the "Algo Trading" button under Xvfb).
# [Experts] Enabled=1 turns on the AutoTrading toolbar button; AllowLiveTrading=1
# is the "Allow Algorithmic Trading" option. A [Common] section is added only when
# MT5_LOGIN is provided, so the terminal can also auto-login. MT5 expects CRLF.
autostart_ini="${WINEPREFIX}/drive_c/mt5-autostart.ini"
autostart_ini_win='C:\mt5-autostart.ini'
{
    if [ -n "${MT5_LOGIN:-}" ]; then
        printf '[Common]\r\n'
        printf 'Login=%s\r\n' "${MT5_LOGIN}"
        printf 'Password=%s\r\n' "${MT5_PASSWORD:-}"
        printf 'Server=%s\r\n' "${MT5_SERVER:-}"
    fi
    printf '[Experts]\r\n'
    printf 'AllowLiveTrading=1\r\n'
    printf 'Enabled=1\r\n'
    printf 'AllowDllImport=1\r\n'
    printf 'Account=0\r\n'
    printf 'Profile=0\r\n'
} > "${autostart_ini}"

wine "${mt5_terminal_path}" "/config:${autostart_ini_win}" >/tmp/mt5-terminal.log 2>&1 &
sleep "${MT5_STARTUP_DELAY:-20}"

host="${GRPC_HOST:-0.0.0.0}"
port="${GRPC_PORT:-50051}"

verbose_args=()
if [ "${GRPC_VERBOSE:-true}" = "true" ]; then
    verbose_args+=(--verbose)
fi

exec wine "${python_exe}" -m mt5_grpc_server.grpc_server \
    --host "${host}" \
    --port "${port}" \
    "${verbose_args[@]}" \
    "$@"
