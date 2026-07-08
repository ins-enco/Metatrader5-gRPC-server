# Contract: Prebuilt container runtime environment

The prebuilt image reuses the existing server's configuration surface so runtime
behavior (logging, redaction, binding, algo-trading) is identical to the
bootstrap image (FR-008, FR-011, Assumption "same runtime behavior"). The only
difference is that **no install/download variables are consulted at runtime** —
Python, VC++, packages, and MT5 are already baked in.

## Runtime environment variables (honored)

| Variable | Default | Meaning |
| --- | --- | --- |
| `MT5_LOGIN` | unset | Account login; when set, an autostart INI `[Common]` section enables MT5 auto-login. |
| `MT5_PASSWORD` | unset | Account password. Redacted in verbose logs. |
| `MT5_SERVER` | unset | Broker server for auto-login. |
| `GRPC_HOST` | `0.0.0.0` | In-container bind address. Keep `0.0.0.0` for Docker publishing. |
| `GRPC_PORT` | `50051` | In-container listen port. |
| `GRPC_VERBOSE` | `true` | Request/response payload logging with secret redaction. |
| `MT5_STARTUP_DELAY` | `20` | Seconds to wait after launching MT5 before starting the gRPC server. |
| `DISPLAY` | `:99` | Xvfb display (set by the image). |
| `XVFB_SCREEN` | `1024x768x16` | Virtual screen for Xvfb. |
| `WINEPREFIX` | `/wineprefix` | Baked-in prefix path. Present as a plain image directory (no volume). |

## Runtime variables intentionally NOT consulted (baked at build)

These exist as **build ARGs** instead (see [`build-args.md`](./build-args.md)).
A container from the prebuilt image MUST run without any of these having effect
at runtime (FR-002):

- `MT5_SETUP_URL` — MT5 already installed at build time (FR-009).
- `MT5_INSTALL_TIMEOUT` — no runtime install to wait on.
- `NUMPY_SPEC` — packages already installed at build time.

## Behavioral guarantees

1. Container serves gRPC without running any Python/VC++/package/MT5 install at
   start (FR-002); ready in < 60s (SC-001).
2. No volume mount required or created (FR-003, SC-003).
3. Verbose logging + secret-field redaction (`password`, `token`, `secret`,
   `api_key`, …) unchanged from the bootstrap image (FR-011).
4. Endpoint defaults to host-local binding via the launcher (`127.0.0.1`);
   exposure is opt-in (FR-013).
5. Offline start: with no outbound access to installer sources, the container
   still starts and serves (spec US1 scenario 3).
