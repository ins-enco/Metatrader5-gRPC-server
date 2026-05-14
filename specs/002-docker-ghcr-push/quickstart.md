# Quickstart: Running the MT5 gRPC Server from GHCR

This guide shows how to pull and run the published Docker image without cloning
the repository or building locally (User Story 3, SC-002).

## Prerequisites

- Docker Engine (or Docker Desktop) on a Linux `amd64` host
- A MetaTrader 5 installer download URL (`MT5_SETUP_URL`)

## Pull the Image

```bash
# Latest stable release
docker pull ghcr.io/ins-enco/mt5-grpc-server:latest

# Specific version
docker pull ghcr.io/ins-enco/mt5-grpc-server:v1.2.3
```

## Run with Docker Compose (recommended)

Create a `docker-compose.yml`:

```yaml
services:
  mt5-grpc-server:
    image: ghcr.io/ins-enco/mt5-grpc-server:latest
    ports:
      - "127.0.0.1:50051:50051"
    environment:
      MT5_SETUP_URL: "https://example.com/mt5setup.exe"   # required
      GRPC_HOST: "0.0.0.0"
      GRPC_PORT: "50051"
      GRPC_VERBOSE: "true"
      NUMPY_SPEC: "numpy<2"
    volumes:
      - wineprefix:/wineprefix
    shm_size: "1gb"
    restart: unless-stopped

volumes:
  wineprefix:
```

Start:

```bash
docker compose up -d
```

## Run with Docker CLI

```bash
docker run -d \
  --name mt5-grpc-server \
  -p 127.0.0.1:50051:50051 \
  -e MT5_SETUP_URL="https://example.com/mt5setup.exe" \
  -e GRPC_HOST="0.0.0.0" \
  -e GRPC_PORT="50051" \
  -v wineprefix:/wineprefix \
  --shm-size=1gb \
  ghcr.io/ins-enco/mt5-grpc-server:latest
```

## First-Run Behaviour

On the first start the entrypoint:
1. Initializes the Wine prefix
2. Installs Python 3.11.9 inside Wine
3. Downloads and installs MetaTrader 5 from `MT5_SETUP_URL`
4. Starts the gRPC server on `GRPC_PORT`

Subsequent starts (with the `/wineprefix` volume intact) skip installation steps
and start the server immediately.

## Verify the Server is Running

```bash
# Check logs
docker logs mt5-grpc-server

# Test gRPC connectivity (requires grpc_health_probe or a gRPC client)
grpc_health_probe -addr=localhost:50051
```

## Trace a Published Image to its Source Commit

```bash
docker inspect ghcr.io/ins-enco/mt5-grpc-server:latest \
  --format '{{ index .Config.Labels "org.opencontainers.image.revision" }}'
```

This returns the full git commit SHA used to build the image (SC-003).
