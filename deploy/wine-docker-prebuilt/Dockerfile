# Prebuilt self-contained MT5 gRPC image.
#
# Unlike the bootstrap image (deploy/wine-docker/Dockerfile), this image bakes
# the Windows Python runtime, the Microsoft VC++ runtime, the MetaTrader 5
# terminal, and the gRPC server packages into a populated /wineprefix at BUILD
# time. Containers therefore start serving gRPC with no runtime install/download
# and require no mounted volume (FR-002, FR-003, SC-001).
FROM ubuntu:24.04

ENV DEBIAN_FRONTEND=noninteractive
ENV WINEARCH=win64
ENV WINEPREFIX=/wineprefix
ENV DISPLAY=:99

# Reproducible build inputs (contracts/build-args.md). Override MT5_SETUP_URL
# with a pinned internal mirror for strict reproducibility (FR-010, SC-006).
ARG PYTHON_VERSION=3.11.9
ARG NUMPY_SPEC=numpy<2
ARG MT5_SETUP_URL=https://download.mql5.com/cdn/web/metaquotes.software.corp/mt5/mt5setup.exe
# Max seconds to wait for the async mt5setup.exe /auto installer to produce
# terminal64.exe before failing the build (parity with entrypoint.sh runtime).
ARG MT5_INSTALL_TIMEOUT=180

# Same apt dependency set as the bootstrap image, for parity.
RUN dpkg --add-architecture i386 \
    && apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        cabextract \
        unzip \
        wget \
        winetricks \
        xvfb \
        wine \
        wine64 \
        wine32 \
        winbind \
    && rm -rf /var/lib/apt/lists/*

RUN mkdir -p /opt/installers \
    && wget -O /opt/installers/python-installer.exe "https://www.python.org/ftp/python/${PYTHON_VERSION}/python-${PYTHON_VERSION}-amd64.exe"

WORKDIR /app

COPY mt5_grpc_proto /app/mt5_grpc_proto
COPY mt5_grpc_server /app/mt5_grpc_server

# Bake the Wine prefix at build time: init prefix, install VC++ runtime, install
# Windows Python, then install the numpy pin and both server packages. This
# lifts deploy/wine-docker/entrypoint.sh first-run steps (lines 49-71) into the
# image so nothing installs at runtime (FR-002, build-args.md invariant 2).
# A headless Xvfb is started for the duration of each RUN that needs a display.
# Each RUN is a separate layer, so stale X locks/sockets left by a prior RUN on
# display :99 must be removed first — otherwise Xvfb refuses to start ("Server is
# already active for display 99") and Wine fails with "no driver could be loaded".
RUN rm -f /tmp/.X99-lock; rm -rf /tmp/.X11-unix; \
    Xvfb :99 -screen 0 1024x768x16 >/tmp/xvfb-build.log 2>&1 & \
    XVFB_PID="$!"; \
    set -e; \
    for i in $(seq 1 30); do [ -S /tmp/.X11-unix/X99 ] && break; sleep 1; done; \
    if [ ! -S /tmp/.X11-unix/X99 ]; then echo "Xvfb failed to start on :99" >&2; cat /tmp/xvfb-build.log >&2; exit 1; fi; \
    wineboot --init; \
    wine reg add "HKEY_CURRENT_USER\\Software\\Wine" /v Version /t REG_SZ /d "win10" /f; \
    WINETRICKS_SUPER_QUIET=1 winetricks -q vcrun2022; \
    wineserver -w; \
    wine /opt/installers/python-installer.exe /quiet InstallAllUsers=1 PrependPath=1 Include_test=0 TargetDir=C:\\Python311; \
    wineserver -w; \
    wine 'C:\Python311\python.exe' -m pip install --upgrade pip setuptools wheel; \
    wine 'C:\Python311\python.exe' -m pip install --force-reinstall "${NUMPY_SPEC}"; \
    wine 'C:\Python311\python.exe' -m pip install -e 'Z:\app\mt5_grpc_proto' -e 'Z:\app\mt5_grpc_server'; \
    wineserver -w; \
    kill "${XVFB_PID}" >/dev/null 2>&1 || true

# Bake the MetaTrader 5 terminal at build time (lifts entrypoint.sh lines 73-87)
# and fail the build if the terminal binary is absent afterwards, so no
# incomplete image is ever produced (FR-009, FR-010, build-args.md invariant 1,
# "build-time source unavailable" edge case). The mt5setup.exe /auto installer
# downloads and installs asynchronously, so — like entrypoint.sh — we poll for
# terminal64.exe (up to MT5_INSTALL_TIMEOUT) rather than checking immediately.
RUN rm -f /tmp/.X99-lock; rm -rf /tmp/.X11-unix; \
    Xvfb :99 -screen 0 1024x768x16 >/tmp/xvfb-mt5.log 2>&1 & \
    XVFB_PID="$!"; \
    for i in $(seq 1 30); do [ -S /tmp/.X11-unix/X99 ] && break; sleep 1; done; \
    if [ ! -S /tmp/.X11-unix/X99 ]; then echo "Xvfb failed to start on :99" >&2; cat /tmp/xvfb-mt5.log >&2; exit 1; fi; \
    wget -O /tmp/mt5setup.exe "${MT5_SETUP_URL}"; \
    wine /tmp/mt5setup.exe /auto || true; \
    wineserver -w || true; \
    rm -f /tmp/mt5setup.exe; \
    mt5_terminal="/wineprefix/drive_c/Program Files/MetaTrader 5/terminal64.exe"; \
    elapsed=0; timeout="${MT5_INSTALL_TIMEOUT:-180}"; \
    while [ ! -f "${mt5_terminal}" ] && [ "${elapsed}" -lt "${timeout}" ]; do \
        sleep 5; elapsed=$((elapsed + 5)); \
    done; \
    kill "${XVFB_PID}" >/dev/null 2>&1 || true; \
    if [ ! -f "${mt5_terminal}" ]; then \
        echo "BUILD FAILED: MetaTrader 5 terminal64.exe was not baked into the prefix after ${timeout}s." >&2; \
        echo "Check MT5_SETUP_URL reachability and the installer's /auto behavior." >&2; \
        exit 1; \
    fi; \
    echo "MetaTrader 5 baked at ${mt5_terminal}"

# Intentionally NO `VOLUME /wineprefix` — the prefix ships as a plain image
# directory to preserve the zero-volume contract (FR-003, SC-003, research.md
# Decision 2). Each container's mutations go to its own writable layer.

COPY deploy/wine-docker-prebuilt/entrypoint.sh /usr/local/bin/mt5-grpc-entrypoint
RUN chmod +x /usr/local/bin/mt5-grpc-entrypoint

EXPOSE 50051

ENTRYPOINT ["/usr/local/bin/mt5-grpc-entrypoint"]
