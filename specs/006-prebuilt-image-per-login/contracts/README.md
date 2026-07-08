# Contracts: Prebuilt Self-Contained Image with Per-Login Containers

**Feature**: `006-prebuilt-image-per-login`

## Protocol / gRPC contract

**No change.** This feature adds zero `.proto` files, RPCs, messages, fields, or
field numbers, and modifies none. It is backward-compatible and wire-format
neutral (see spec "Protocol and MT5 Contract Impact"). Existing clients and the
existing bootstrap deployment are unaffected.

The external interfaces this feature *does* introduce are operator-facing
deployment contracts, documented below and in the sibling files:

- [`launcher-cli.md`](./launcher-cli.md) — the per-login launcher command surface.
- [`container-env.md`](./container-env.md) — the container runtime environment
  variable contract.
- [`build-args.md`](./build-args.md) — the prebuilt image build-argument contract.

These are the "user/other-system interfaces" the project exposes for this
feature; there is no library API or new endpoint to specify.
