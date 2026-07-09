#!/usr/bin/env bash
# Runtime entrypoint for the self-contained image. Everything (Wine prefix,
# Python, packages, MetaTrader 5) is already baked into the image, so this only
# starts Xvfb, writes the MT5 autostart config and launches the terminal + the
# gRPC server. No installation, no markers, no volume required.
set -euo pipefail

display="${DISPLAY:-:99}"
screen="${XVFB_SCREEN:-1024x768x16}"

Xvfb "${display}" -screen 0 "${screen}" >/tmp/xvfb.log 2>&1 &
xvfb_pid="$!"
cleanup() {
    kill "${xvfb_pid}" >/dev/null 2>&1 || true
}
trap cleanup EXIT

sleep 2

# Allow overriding the whole command (e.g. an interactive shell for debugging).
if [ "$#" -gt 0 ]; then
    exec "$@"
fi

python_exe='C:\Python311\python.exe'
mt5_terminal_path="${MT5_TERMINAL_PATH:-C:\Program Files\MetaTrader 5\terminal64.exe}"

# The prefix is baked in, but wineserver still has to be started for this
# container instance.
wineboot --init
wineserver -w

# Generate an MT5 startup config so the headless terminal enables AutoTrading
# automatically (there is no GUI to click the "Algo Trading" button under Xvfb).
# A [Common] section is added only when MT5_LOGIN is provided. MT5 expects CRLF.
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
if [ "${GRPC_VERBOSE:-false}" = "true" ]; then
    verbose_args+=(--verbose)
fi

exec wine "${python_exe}" -m mt5_grpc_server.grpc_server \
    --host "${host}" \
    --port "${port}" \
    "${verbose_args[@]}" \
    "$@"
