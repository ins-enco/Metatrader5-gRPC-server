"""Wine-side half of the MT5 bridge.

This runs under Wine's Python, beside terminal64.exe, because the MetaTrader5
package is Windows-only and has to share a process with the terminal. It does
nothing else: every socket that faces the outside world belongs to the Linux
half, which is the whole point of the split.

Requests are served one at a time on purpose. MT5 is a single-session API -
``login`` switches the terminal to one account - so concurrency here would buy
nothing and would make the account a shared mutable.
"""
import logging
import os
import socket
import sys

from . import protocol
from .calls import ALLOWED_CALLS

logger = logging.getLogger(__name__)

DEFAULT_BIND = "127.0.0.1:50060"


def _error(request_id, kind, message):
    return {"id": request_id, "ok": False, "error": {"kind": kind, "message": message}}


def dispatch(mt5, request):
    """Run one request against the MetaTrader5 module and build the reply."""
    request_id = request.get("id")
    name = request.get("fn")

    if name not in ALLOWED_CALLS:
        return _error(request_id, "NotAllowed", "call %r is not on the allowlist" % (name,))

    fn = getattr(mt5, name, None)
    if not callable(fn):
        return _error(request_id, "Missing", "MetaTrader5 has no callable %r" % (name,))

    try:
        result = fn(*request.get("args") or [], **request.get("kwargs") or {})
    except Exception as exc:  # the worker must survive anything MT5 raises
        logger.exception("MT5 call %s failed", name)
        return _error(request_id, type(exc).__name__, str(exc))

    return {"id": request_id, "ok": True, "result": protocol.to_jsonable(result)}


def serve_connection(mt5, conn):
    """Answer requests on one connection until the peer goes away."""
    while True:
        try:
            request = protocol.recv(conn)
        except (ConnectionError, OSError):
            return
        except protocol.ProtocolError as exc:
            logger.warning("dropping bridge connection: %s", exc)
            return

        try:
            protocol.send(conn, dispatch(mt5, request))
        except (ConnectionError, OSError):
            return


def _split_bind(value):
    host, _, port = value.rpartition(":")
    return host or "127.0.0.1", int(port)


def main(argv=None):
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s - %(name)s - %(levelname)s - %(message)s",
    )
    argv = sys.argv[1:] if argv is None else argv
    host, port = _split_bind(argv[0] if argv else os.environ.get("BRIDGE_BIND", DEFAULT_BIND))

    import MetaTrader5 as mt5

    listener = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    listener.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    listener.bind((host, port))
    listener.listen(1)
    logger.info("MT5 bridge worker listening on %s:%d", host, port)

    while True:
        conn, peer = listener.accept()
        conn.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        logger.info("bridge client connected from %s", peer)
        try:
            serve_connection(mt5, conn)
        finally:
            conn.close()
            logger.info("bridge client disconnected")


if __name__ == "__main__":
    main()
