# MT5 gRPC Server in Docker with Wine

This deployment runs Windows Python inside Wine because the `MetaTrader5`
Python package is Windows-native. A normal Linux Python container is not enough.

## Build

```bash
docker compose -f deploy/wine-docker/docker-compose.yml build
```

## Run

```bash
docker compose -f deploy/wine-docker/docker-compose.yml up -d
```

On first start, the entrypoint installs Windows Python and the local patched
server packages into the persistent Wine volume. It also sets Wine to Windows
10, forces native Microsoft runtime DLL overrides, installs the Microsoft VC++
runtime with `winetricks vcrun2022`, installs Python packages with
`NUMPY_SPEC` defaulting to `numpy<2`, downloads MetaTrader 5 from
`MT5_SETUP_URL`, installs it into `/wineprefix`, verifies
`/wineprefix/drive_c/Program Files/MetaTrader 5/terminal64.exe`, then starts
MT5 and the Python gRPC server. Later starts reuse the same Wine prefix.

If the MT5 installer returns a non-zero Wine exit code, the entrypoint still
waits up to `MT5_INSTALL_TIMEOUT` seconds and continues if `terminal64.exe`
appears. Increase `MT5_INSTALL_TIMEOUT` on slow hosts.

The server listens on the host at `127.0.0.1:50051`.

Verbose logging is enabled by default in the compose file. The patched server
redacts fields named `password`, `token`, `secret`, `api_key`, and similar
secret names before writing request/response payloads.

To disable request/response payload logging entirely, set:

```yaml
GRPC_VERBOSE: "false"
```

## Environment

Configure these values in `deploy/wine-docker/docker-compose.yml`.

| Variable | Default | Description |
| --- | --- | --- |
| `WINEPREFIX` | `/wineprefix` | Persistent Wine prefix used for Windows Python, VC++ runtime, MT5, and package state. Keep this aligned with the `wineprefix:/wineprefix` volume mount. |
| `GRPC_HOST` | `0.0.0.0` | Address the Python gRPC server binds inside the container. Keep `0.0.0.0` for Docker port publishing. |
| `GRPC_PORT` | `50051` | Port the Python gRPC server listens on inside the container. If changed, update the compose `ports` mapping too. |
| `GRPC_VERBOSE` | `true` | Enables request/response payload logging. Secret-like fields are redacted. Set to `false` to disable payload logging entirely. |
| `NUMPY_SPEC` | `numpy<2` | NumPy pip requirement installed into Windows Python. The default avoids newer NumPy wheels that can hit Wine/UCRT compatibility problems. |
| `MT5_SETUP_URL` | `https://download.mql5.com/cdn/web/metaquotes.software.corp/mt5/mt5setup.exe` | MetaTrader 5 Windows installer URL downloaded on first startup when MT5 is missing. Override this to use a pinned/internal installer mirror. |
| `MT5_TERMINAL_PATH` | `C:\Program Files\MetaTrader 5\terminal64.exe` | Windows path used by Wine to start MT5. Keep this in sync with the installed MT5 location. |
| `MT5_INSTALL_TIMEOUT` | `600` | Seconds to wait for `/wineprefix/drive_c/Program Files/MetaTrader 5/terminal64.exe` after the installer exits. Increase on slow hosts. |
| `MT5_STARTUP_DELAY` | `20` | Seconds to wait after launching MT5 before starting the Python gRPC server. Increase if MT5 starts slowly. |

The entrypoint also honors these image/runtime variables if you need to tune the
headless Wine display:

| Variable | Default | Description |
| --- | --- | --- |
| `DISPLAY` | `:99` | X display used by `Xvfb` and Wine. The image sets this by default. |
| `XVFB_SCREEN` | `1024x768x16` | Virtual screen passed to `Xvfb`. Increase resolution or color depth only if MT5 needs it. |

## Notes

- Use Windows Python inside Wine, not native Linux Python.
- The VC++ runtime bootstrap is required for Windows Python/native packages
  that call UCRT functions missing from Wine's built-in `ucrtbase.dll`.
- The container image does not redistribute MetaTrader 5. The entrypoint
  downloads the installer on first startup.
- Keep the port bound to `127.0.0.1` unless remote machines must connect.
- If remote machines must connect, use TLS or restrict access with a firewall.
