#!/usr/bin/env bash
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

if [ "$#" -gt 0 ]; then
    exec "$@"
fi

python_exe='C:\Python311\python.exe'
ready_marker="${WINEPREFIX}/.mt5-grpc-python-ready"
packages_marker="${WINEPREFIX}/.mt5-grpc-python-packages-v2-ready"
vcrun_marker="${WINEPREFIX}/.mt5-grpc-vcrun2022-ready"
numpy_spec="${NUMPY_SPEC:-numpy<2}"
mt5_setup_url="${MT5_SETUP_URL:-https://download.mql5.com/cdn/web/metaquotes.software.corp/mt5/mt5setup.exe}"
mt5_terminal_path="${MT5_TERMINAL_PATH:-C:\Program Files\MetaTrader 5\terminal64.exe}"
mt5_terminal_file="${WINEPREFIX}/drive_c/Program Files/MetaTrader 5/terminal64.exe"
mt5_installer="/tmp/mt5setup.exe"
mt5_install_timeout="${MT5_INSTALL_TIMEOUT:-180}"

wait_for_file() {
    local path="$1"
    local timeout="$2"
    local elapsed=0

    while [ "${elapsed}" -lt "${timeout}" ]; do
        if [ -f "${path}" ]; then
            return 0
        fi

        sleep 5
        elapsed=$((elapsed + 5))
    done

    return 1
}

wineboot --init
wine reg add "HKEY_CURRENT_USER\\Software\\Wine" /v Version /t REG_SZ /d "win10" /f


if [ ! -f "${vcrun_marker}" ]; then
    echo "Installing Microsoft VC++ runtime into Wine prefix."
    WINETRICKS_SUPER_QUIET=1 winetricks -q vcrun2022
    wineserver -w
    touch "${vcrun_marker}"
fi

if [ ! -f "${ready_marker}" ]; then
    wine /opt/installers/python-installer.exe /quiet InstallAllUsers=1 PrependPath=1 Include_test=0 TargetDir=C:\\Python311
    wineserver -w
    touch "${ready_marker}"
fi

if [ ! -f "${packages_marker}" ]; then
    wine "${python_exe}" -m pip install --upgrade pip setuptools wheel
    wine "${python_exe}" -m pip install --force-reinstall "${numpy_spec}"
    wine "${python_exe}" -m pip install -e 'Z:\app\mt5_grpc_proto' -e 'Z:\app\mt5_grpc_server'
    touch "${packages_marker}"
fi

if [ ! -f "${mt5_terminal_file}" ]; then
    echo "MetaTrader 5 is missing. Downloading installer from ${mt5_setup_url}."
    wget -O "${mt5_installer}" "${mt5_setup_url}"

    installer_exit=0
    wine "${mt5_installer}" /auto || installer_exit="$?"
    wineserver -w || true
    rm -f "${mt5_installer}"

    if [ "${installer_exit}" -ne 0 ]; then
        echo "MetaTrader 5 installer exited with code ${installer_exit}; checking whether installation completed anyway."
    fi

    wait_for_file "${mt5_terminal_file}" "${mt5_install_timeout}" || true
fi

if [ ! -f "${mt5_terminal_file}" ]; then
    echo "MetaTrader 5 installation failed: ${mt5_terminal_file} was not found after ${mt5_install_timeout}s." >&2
    echo "Try increasing MT5_INSTALL_TIMEOUT or run an interactive shell to inspect /wineprefix." >&2
    exit 1
fi

echo "MetaTrader 5 found at ${mt5_terminal_file}."
wine "${mt5_terminal_path}" >/tmp/mt5-terminal.log 2>&1 &
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
