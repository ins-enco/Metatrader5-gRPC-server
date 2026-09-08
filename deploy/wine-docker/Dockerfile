FROM ubuntu:24.04

ENV DEBIAN_FRONTEND=noninteractive
ENV WINEARCH=win64
ENV WINEPREFIX=/wineprefix
ENV DISPLAY=:99

ARG PYTHON_VERSION=3.11.9

# Wine is pinned deliberately. Ubuntu 24.04 ships Wine 9.0, under which grpcio
# aborts the whole process on a partial socket write (a CHECK in
# windows_endpoint.cc). Leaving it unpinned also means any rebuild silently
# swaps the production runtime - see the commit message for measurements.
ARG WINE_VERSION=10.0.0.0~noble-1

RUN dpkg --add-architecture i386 \
    && apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        cabextract \
        unzip \
        wget \
        winetricks \
        xvfb \
        winbind \
    && mkdir -pm755 /etc/apt/keyrings \
    && wget -qO /etc/apt/keyrings/winehq-archive.key https://dl.winehq.org/wine-builds/winehq.key \
    && wget -qNP /etc/apt/sources.list.d/ https://dl.winehq.org/wine-builds/ubuntu/dists/noble/winehq-noble.sources \
    && apt-get update \
    && apt-get install -y --install-recommends \
        winehq-stable=${WINE_VERSION} \
        wine-stable=${WINE_VERSION} \
        wine-stable-amd64=${WINE_VERSION} \
        wine-stable-i386=${WINE_VERSION} \
    && rm -rf /var/lib/apt/lists/*

RUN mkdir -p /opt/installers \
    && wget -O /opt/installers/python-installer.exe "https://www.python.org/ftp/python/${PYTHON_VERSION}/python-${PYTHON_VERSION}-amd64.exe"

WORKDIR /app

COPY mt5_grpc_proto /app/mt5_grpc_proto
COPY mt5_grpc_server /app/mt5_grpc_server

COPY deploy/wine-docker/entrypoint.sh /usr/local/bin/mt5-grpc-entrypoint
RUN chmod +x /usr/local/bin/mt5-grpc-entrypoint

EXPOSE 50051

ENTRYPOINT ["/usr/local/bin/mt5-grpc-entrypoint"]
