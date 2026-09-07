"""Where service implementations get their MT5 handle.

Set ``MT5_BRIDGE=1`` and every MT5 call is forwarded to the Wine-side worker
over the bridge. Leave it unset and the real MetaTrader5 module is imported
in-process, exactly as before - so the flag is the whole of the migration
switch, and turning it off is the whole of the rollback.

The two paths are interchangeable at the call site: the bridge returns records
carrying the same attribute names MT5 uses.
"""
import os

_TRUTHY = ("1", "true", "yes", "on")


def bridge_enabled():
    return os.environ.get("MT5_BRIDGE", "").strip().lower() in _TRUTHY


def load():
    if bridge_enabled():
        from .bridge.client import BridgeMT5
        return BridgeMT5()
    import MetaTrader5 as real_mt5
    return real_mt5


mt5 = load()
