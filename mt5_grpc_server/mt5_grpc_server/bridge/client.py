"""Linux-side half of the MT5 bridge.

``BridgeMT5`` exposes the same call names as the MetaTrader5 module and
forwards each one to the Wine-side worker, so a service implementation only
has to change where it imports ``mt5`` from.

Connections are made lazily and re-made after a failure. That is what lets the
gRPC server bind its port and start answering before the terminal is ready:
callers get ``BridgeUnavailable`` - which the service layer maps to
``UNAVAILABLE`` - instead of a dead TCP connection.
"""
import itertools
import logging
import os
import socket
import threading

from . import protocol
from .calls import IDEMPOTENT_CALLS

logger = logging.getLogger(__name__)

DEFAULT_ADDR = "127.0.0.1:50060"


class BridgeUnavailable(Exception):
    """The Wine-side worker could not be reached for this call."""


class BridgeCallError(Exception):
    """The worker reached MT5 and MT5 (or the allowlist) refused."""

    def __init__(self, kind, message):
        super().__init__("%s: %s" % (kind, message))
        self.kind = kind
        self.message = message


class Record(object):
    """Attribute access over a dict.

    MT5 returns namedtuples and the service code reads ``deal.ticket``. Over
    the bridge the same value arrives as JSON, so it is wrapped here rather
    than making every caller learn the difference.
    """

    __slots__ = ("_data",)

    def __init__(self, data):
        self._data = data

    def __getattr__(self, name):
        if name == "_data":  # only reachable before __init__ ran
            raise AttributeError(name)
        try:
            return self._data[name]
        except KeyError:
            raise AttributeError(name)

    def _asdict(self):
        return dict(self._data)

    def __repr__(self):
        return "Record(%r)" % (self._data,)


def rehydrate(value):
    """Turn decoded JSON back into what the service layer expects.

    Objects become attribute-accessible records; arrays stay lists, because
    call sites unpack them (``code, message = mt5.last_error()``) and slice
    them.
    """
    if isinstance(value, dict):
        return Record(value)
    if isinstance(value, list):
        return [rehydrate(item) for item in value]
    return value


def _split_addr(value):
    host, _, port = value.rpartition(":")
    return host or "127.0.0.1", int(port)


class BridgeClient(object):
    """One connection to the worker, one call at a time."""

    def __init__(self, address=None, call_timeout=None, connect_timeout=None):
        host, port = _split_addr(address or os.environ.get("BRIDGE_ADDR", DEFAULT_ADDR))
        self._host = host
        self._port = port
        self._call_timeout = float(
            call_timeout if call_timeout is not None
            else os.environ.get("BRIDGE_CALL_TIMEOUT", "60"))
        self._connect_timeout = float(
            connect_timeout if connect_timeout is not None
            else os.environ.get("BRIDGE_CONNECT_TIMEOUT", "5"))
        self._lock = threading.Lock()
        self._ids = itertools.count(1)
        self._sock = None

    @property
    def address(self):
        return "%s:%d" % (self._host, self._port)

    def close(self):
        with self._lock:
            self._drop()

    def _drop(self):
        if self._sock is not None:
            try:
                self._sock.close()
            except OSError:
                pass
            self._sock = None

    def _connect(self):
        try:
            sock = socket.create_connection(
                (self._host, self._port), timeout=self._connect_timeout)
        except OSError as exc:
            raise BridgeUnavailable(
                "cannot reach the MT5 bridge at %s: %s" % (self.address, exc))
        sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        sock.settimeout(self._call_timeout)
        self._sock = sock

    def call(self, fn, *args, **kwargs):
        """Forward one MT5 call. Raises BridgeUnavailable or BridgeCallError."""
        retriable = fn in IDEMPOTENT_CALLS
        attempts = 2 if retriable else 1

        with self._lock:
            for attempt in range(1, attempts + 1):
                if self._sock is None:
                    self._connect()

                request = {"id": next(self._ids), "fn": fn,
                           "args": list(args), "kwargs": kwargs}
                try:
                    protocol.send(self._sock, request)
                    response = protocol.recv(self._sock)
                except (ConnectionError, OSError, protocol.ProtocolError) as exc:
                    self._drop()
                    if attempt < attempts:
                        logger.warning(
                            "retrying idempotent MT5 call %s after transport error: %s",
                            fn, exc)
                        continue
                    raise BridgeUnavailable("MT5 bridge call %r failed: %s" % (fn, exc))

                if response.get("id") != request["id"]:
                    self._drop()
                    raise BridgeUnavailable(
                        "MT5 bridge replied to the wrong request; connection dropped")

                if not response.get("ok"):
                    error = response.get("error") or {}
                    raise BridgeCallError(
                        error.get("kind", "Error"), error.get("message", ""))

                return rehydrate(response.get("result"))


class BridgeMT5(object):
    """Stands in for the MetaTrader5 module."""

    def __init__(self, client=None):
        self._client = client or BridgeClient()

    @property
    def client(self):
        return self._client

    def __getattr__(self, name):
        if name.startswith("_"):
            raise AttributeError(name)
        client = self._client

        def call(*args, **kwargs):
            return client.call(name, *args, **kwargs)

        call.__name__ = str(name)
        return call
