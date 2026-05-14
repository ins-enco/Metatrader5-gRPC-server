FROM ubuntu:24.04

ENV DEBIAN_FRONTEND=noninteractive
ENV WINEARCH=win64
ENV WINEPREFIX=/wineprefix
ENV DISPLAY=:99

ARG PYTHON_VERSION=3.11.9

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

COPY deploy/wine-docker/entrypoint.sh /usr/local/bin/mt5-grpc-entrypoint
RUN chmod +x /usr/local/bin/mt5-grpc-entrypoint

EXPOSE 50051

ENTRYPOINT ["/usr/local/bin/mt5-grpc-entrypoint"]
