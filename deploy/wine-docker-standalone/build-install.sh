#!/usr/bin/env bash
# Build-time installer. Runs once during `docker build` and bakes the complete
# Wine prefix into the image: VC++ runtime, Python, the mt5_grpc_* packages and
# MetaTrader 5. At runtime nothing else has to be installed.
set -euo pipefail

display="${DISPLAY:-:99}"
screen="${XVFB_SCREEN:-1024x768x16}"
numpy_spec="${NUMPY_SPEC:-numpy<2}"
mt5_setup_url="${MT5_SETUP_URL:-https://download.mql5.com/cdn/web/metaquotes.software.corp/mt5/mt5setup.exe}"
mt5_install_timeout="${MT5_INSTALL_TIMEOUT:-600}"
mt5_terminal_file="${WINEPREFIX}/drive_c/Program Files/MetaTrader 5/terminal64.exe"
mt5_installer="/tmp/mt5setup.exe"
python_exe='C:\Python311\python.exe'

# winetricks and the MT5 installer need an X display even for silent installs.
Xvfb "${display}" -screen 0 "${screen}" >/tmp/xvfb-build.log 2>&1 &
xvfb_pid="$!"
cleanup() {
    kill "${xvfb_pid}" >/dev/null 2>&1 || true
}
trap cleanup EXIT

sleep 2

echo "==> Initialising Wine prefix"
wineboot --init
wineserver -w
wine reg add "HKEY_CURRENT_USER\\Software\\Wine" /v Version /t REG_SZ /d "win10" /f

echo "==> Installing Microsoft VC++ 2022 runtime"
WINETRICKS_SUPER_QUIET=1 winetricks -q vcrun2022
wineserver -w

echo "==> Installing Python ${PYTHON_VERSION:-} into the Wine prefix"
wine /opt/installers/python-installer.exe /quiet InstallAllUsers=1 PrependPath=1 Include_test=0 TargetDir=C:\\Python311
wineserver -w

echo "==> Installing Python packages"
wine "${python_exe}" -m pip install --upgrade pip setuptools wheel
wine "${python_exe}" -m pip install --force-reinstall "${numpy_spec}"
wine "${python_exe}" -m pip install -e 'Z:\app\mt5_grpc_proto' -e 'Z:\app\mt5_grpc_server'
wineserver -w

echo "==> Installing MetaTrader 5 from ${mt5_setup_url}"
wget -O "${mt5_installer}" "${mt5_setup_url}"

installer_exit=0
wine "${mt5_installer}" /auto || installer_exit="$?"
wineserver -w || true
rm -f "${mt5_installer}"

if [ "${installer_exit}" -ne 0 ]; then
    echo "MetaTrader 5 installer exited with code ${installer_exit}; checking whether installation completed anyway."
fi

elapsed=0
while [ "${elapsed}" -lt "${mt5_install_timeout}" ]; do
    if [ -f "${mt5_terminal_file}" ]; then
        break
    fi
    sleep 5
    elapsed=$((elapsed + 5))
done

if [ ! -f "${mt5_terminal_file}" ]; then
    echo "MetaTrader 5 installation failed: ${mt5_terminal_file} was not found after ${mt5_install_timeout}s." >&2
    echo "Increase the MT5_INSTALL_TIMEOUT build-arg and rebuild." >&2
    exit 1
fi

# Warm-up: run the freshly installed terminal once so it downloads and applies
# its own self-update BEFORE we bake the image. Without this, the first real run
# inside a container would have to self-update terminal64.exe while it is being
# used. Non-fatal: a crash here must not fail the build.
mt5_warmup_delay="${MT5_WARMUP_DELAY:-120}"
echo "==> Warming up MetaTrader 5 (self-update) for ${mt5_warmup_delay}s"
wine "${mt5_terminal_file}" /portable >/tmp/mt5-warmup.log 2>&1 &
warmup_pid="$!"
sleep "${mt5_warmup_delay}"
wineserver -k >/dev/null 2>&1 || true
kill "${warmup_pid}" >/dev/null 2>&1 || true
wait "${warmup_pid}" 2>/dev/null || true

echo "==> MetaTrader 5 baked into image at ${mt5_terminal_file}"
