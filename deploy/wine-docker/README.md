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

## Notes

- Use Windows Python inside Wine, not native Linux Python.
- The VC++ runtime bootstrap is required for Windows Python/native packages
  that call UCRT functions missing from Wine's built-in `ucrtbase.dll`.
- The container image does not redistribute MetaTrader 5. The entrypoint
  downloads the installer on first startup.
- Keep the port bound to `127.0.0.1` unless remote machines must connect.
- If remote machines must connect, use TLS or restrict access with a firewall.
