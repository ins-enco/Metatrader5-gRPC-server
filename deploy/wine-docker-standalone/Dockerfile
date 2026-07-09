# Self-contained MT5 gRPC server image.
#
# Unlike deploy/wine-docker/Dockerfile (which installs the Wine prefix, Python,
# the pip packages and MetaTrader 5 at *runtime* into a persistent volume), this
# image bakes EVERYTHING into the image at build time. The resulting image needs
# no volume: any number of containers can be started from it and run fully
# independently, each with its own copy-on-write Wine prefix.
FROM ubuntu:24.04

ENV DEBIAN_FRONTEND=noninteractive
ENV WINEARCH=win64
ENV WINEPREFIX=/wineprefix
ENV DISPLAY=:99

ARG PYTHON_VERSION=3.11.9
ARG NUMPY_SPEC="numpy<2"
ARG MT5_SETUP_URL="https://download.mql5.com/cdn/web/metaquotes.software.corp/mt5/mt5setup.exe"
ARG MT5_INSTALL_TIMEOUT=600
ARG MT5_WARMUP_DELAY=120
# Wine branch from the official WineHQ repo. Ubuntu 24.04's own `wine` package is
# too old for current MetaTrader 5 builds (terminal64.exe crashes with an
# "Unhandled illegal instruction"), so we pull a modern Wine from WineHQ instead.
# Use `stable` (production, boots reliably headless) rather than `staging` --
# staging is a testing branch and its wineboot hangs during a headless build.
ARG WINE_BRANCH=stable

# Install a modern Wine from WineHQ (Ubuntu 24.04 = "noble").
RUN dpkg --add-architecture i386 \
    && apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates wget gnupg \
    && mkdir -pm755 /etc/apt/keyrings \
    && wget -O /etc/apt/keyrings/winehq-archive.key https://dl.winehq.org/wine-builds/winehq.key \
    && wget -NP /etc/apt/sources.list.d/ https://dl.winehq.org/wine-builds/ubuntu/dists/noble/winehq-noble.sources \
    && apt-get update \
    && apt-get install -y --install-recommends "winehq-${WINE_BRANCH}" \
    && apt-get install -y --no-install-recommends \
        cabextract \
        unzip \
        winetricks \
        xvfb \
        winbind \
    && rm -rf /var/lib/apt/lists/*

RUN mkdir -p /opt/installers \
    && wget -O /opt/installers/python-installer.exe "https://www.python.org/ftp/python/${PYTHON_VERSION}/python-${PYTHON_VERSION}-amd64.exe"

WORKDIR /app

COPY mt5_grpc_proto /app/mt5_grpc_proto
COPY mt5_grpc_server /app/mt5_grpc_server

# Bake the whole Wine prefix (VC++ runtime, Python, pip packages, MetaTrader 5)
# into the image so nothing has to be installed at runtime.
COPY deploy/wine-docker-standalone/build-install.sh /tmp/build-install.sh
RUN chmod +x /tmp/build-install.sh \
    && NUMPY_SPEC="${NUMPY_SPEC}" \
       MT5_SETUP_URL="${MT5_SETUP_URL}" \
       MT5_INSTALL_TIMEOUT="${MT5_INSTALL_TIMEOUT}" \
       MT5_WARMUP_DELAY="${MT5_WARMUP_DELAY}" \
       /tmp/build-install.sh \
    && rm -f /tmp/build-install.sh

COPY deploy/wine-docker-standalone/entrypoint.sh /usr/local/bin/mt5-grpc-entrypoint
RUN chmod +x /usr/local/bin/mt5-grpc-entrypoint

EXPOSE 50051

ENTRYPOINT ["/usr/local/bin/mt5-grpc-entrypoint"]
