"""Shared test setup for the server suite.

**Stub out MetaTrader5 before any test module imports a server implementation.**

The MT5 Python package is Windows-only and talks to a live terminal, so no test
may bind the real thing. Individual test modules already do
``sys.modules['MetaTrader5'] = MagicMock()`` at import time, but that only works
for a module that imports a server implementation *first* -- and
``mt5_grpc_server/imp/__init__.py`` star-imports **every** submodule, so importing
any one of them (``deals_history``, ``trade_events``, ...) binds ``mt5`` in all of
them at once. Whichever test module pytest collects first therefore decided what
the whole suite got, and a new test file could silently flip it by sorting
earlier: the module bound the real MetaTrader5, and
``test_trade_action_validation``'s ``trade_impl.mt5.reset_mock()`` then failed
with AttributeError.

conftest.py is imported before any test module, so installing the stub here makes
the outcome independent of collection order. Per-module assignments still work
unchanged -- they simply replace a stub with another stub, and a module that
captured ``<impl>.mt5`` at import time keeps a usable MagicMock either way.
"""
import sys
from unittest.mock import MagicMock

sys.modules.setdefault("MetaTrader5", MagicMock())
