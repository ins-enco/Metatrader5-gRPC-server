"""Round-trip tests for the MT5 bridge, with MetaTrader5 stubbed out.

A worker is run on a loopback port against a fake MetaTrader5 module, and
``GetDeals`` is served twice - once calling the fake directly, once through the
bridge - so the two paths can be compared byte for byte.

Also covers: frame slicing, NaN sanitising, the call allowlist, reconnection
after the worker drops a connection, and the UNAVAILABLE mapping when no
worker is listening.
"""
import os
import socket
import sys
import threading
from collections import namedtuple

import pytest

_REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
for _p in (os.path.join(_REPO_ROOT, "mt5_grpc_proto"), os.path.join(_REPO_ROOT, "mt5_grpc_server")):
    if _p not in sys.path:
        sys.path.insert(0, _p)

FakeDeal = namedtuple(
    "FakeDeal",
    "ticket order time time_msc type entry magic position_id reason volume price "
    "commission swap profit fee symbol comment external_id",
)


def make_deals(count):
    return tuple(
        FakeDeal(
            ticket=18332204 + i, order=41230000 + i, time=1765444587 + i,
            time_msc=(1765444587 + i) * 1000, type=1, entry=0, magic=12345,
            position_id=35620102 + i, reason=3, volume=0.13, price=155.246,
            commission=0.0, swap=0.0, profit=1000.0, fee=0.0,
            symbol="USDJPY", comment="TC#269030659", external_id=str(11827764 + i),
        )
        for i in range(count)
    )


class FakeMT5(object):
    """Just enough of the MetaTrader5 surface for these tests."""

    def __init__(self, deals=()):
        self.deals = deals
        self.error = (-1, "no error")
        self.calls = []

    def history_deals_get(self, *args, **kwargs):
        self.calls.append(("history_deals_get", args, kwargs))
        return self.deals

    def last_error(self):
        return self.error

    def order_send(self, request):
        self.calls.append(("order_send", request))
        return {"retcode": 10009}


_fake_module = FakeMT5()
sys.modules.setdefault("MetaTrader5", _fake_module)

from mt5_grpc_server.bridge import protocol  # noqa: E402
from mt5_grpc_server.bridge import worker  # noqa: E402
from mt5_grpc_server.bridge.client import (  # noqa: E402
    BridgeCallError,
    BridgeClient,
    BridgeMT5,
    BridgeUnavailable,
)
from mt5_grpc_server.imp import deals_history  # noqa: E402
from mt5_grpc_proto.deal_pb2 import DealsRequest  # noqa: E402


class WorkerHarness(object):
    """Runs worker.serve_connection on a loopback port, accepting repeatedly."""

    def __init__(self, mt5):
        self.mt5 = mt5
        self._listener = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self._listener.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self._listener.bind(("127.0.0.1", 0))
        self._listener.listen(2)
        self._listener.settimeout(0.5)
        self.port = self._listener.getsockname()[1]
        self.connections = 0
        self._stop = threading.Event()
        self._thread = threading.Thread(target=self._loop, daemon=True)
        self._thread.start()

    def _loop(self):
        while not self._stop.is_set():
            try:
                conn, _ = self._listener.accept()
            except socket.timeout:
                continue
            except OSError:
                return
            self.connections += 1
            try:
                worker.serve_connection(self.mt5, conn)
            finally:
                conn.close()

    def stop(self):
        self._stop.set()
        self._thread.join(timeout=3)
        self._listener.close()


class Ctx(object):
    """Minimal gRPC context stand-in."""

    def __init__(self):
        self.aborted = None

    def is_active(self):
        return True

    def abort(self, code, details):
        self.aborted = (code, details)
        raise RuntimeError("aborted: %s" % (details,))


@pytest.fixture
def fake():
    return FakeMT5(make_deals(1571))


@pytest.fixture
def harness(fake):
    h = WorkerHarness(fake)
    yield h
    h.stop()


@pytest.fixture
def bridged(harness):
    client = BridgeClient(address="127.0.0.1:%d" % harness.port,
                          call_timeout=10, connect_timeout=2)
    yield BridgeMT5(client)
    client.close()


def _get_deals(mt5_handle, monkeypatch):
    monkeypatch.setattr(deals_history, "mt5", mt5_handle)
    request = DealsRequest()
    request.time_filter.date_from = 0
    request.time_filter.date_to = 1788857154
    return deals_history.TradeHistoryServiceImpl().GetDeals(request, Ctx())


def test_bridged_getdeals_matches_direct(fake, bridged, monkeypatch):
    direct = _get_deals(fake, monkeypatch)
    through_bridge = _get_deals(bridged, monkeypatch)

    assert len(direct.deals) == 1571
    assert direct.SerializeToString() == through_bridge.SerializeToString()


def test_frames_never_exceed_frame_bytes():
    class Recorder(object):
        def __init__(self):
            self.sizes = []
            self.blob = b""

        def sendall(self, chunk):
            self.sizes.append(len(chunk))
            self.blob += chunk

    payload = {"id": 1, "ok": True,
               "result": [protocol.to_jsonable(d) for d in make_deals(1571)]}
    rec = Recorder()
    protocol.send(rec, payload)

    assert len(rec.blob) > protocol.FRAME_BYTES, "payload should need several frames"
    assert max(rec.sizes) <= protocol.FRAME_BYTES
    assert len(rec.sizes) > 1


def test_nan_and_infinity_become_null():
    cleaned = protocol.to_jsonable({"a": float("nan"), "b": float("inf"), "c": 1.5})
    assert cleaned == {"a": None, "b": None, "c": 1.5}
    protocol.encode({"result": cleaned})  # allow_nan=False must not raise


def test_last_error_survives_tuple_unpacking(fake, bridged):
    fake.error = (-10005, "IPC timeout")
    code, message = bridged.last_error()
    assert (code, message) == (-10005, "IPC timeout")


def test_call_outside_allowlist_is_refused(bridged):
    with pytest.raises(BridgeCallError) as excinfo:
        bridged.some_internal_helper()
    assert excinfo.value.kind == "NotAllowed"


def test_idempotent_call_reconnects_after_worker_drops(fake, bridged, harness):
    assert len(bridged.history_deals_get(0, 1)) == 1571
    first = harness.connections

    bridged.client._sock.close()  # simulate the worker going away while idle

    assert len(bridged.history_deals_get(0, 1)) == 1571
    assert harness.connections > first, "client should have reconnected"


def test_no_worker_reports_unavailable(monkeypatch):
    dead = BridgeMT5(BridgeClient(address="127.0.0.1:1", connect_timeout=1))
    with pytest.raises(BridgeUnavailable):
        dead.history_deals_get(0, 1)

    ctx = Ctx()
    monkeypatch.setattr(deals_history, "mt5", dead)
    request = DealsRequest()
    request.time_filter.date_to = 1788857154
    with pytest.raises(RuntimeError):
        deals_history.TradeHistoryServiceImpl().GetDeals(request, ctx)
    assert ctx.aborted is not None
    assert "UNAVAILABLE" in str(ctx.aborted[0])


def test_order_send_is_not_retriable():
    from mt5_grpc_server.bridge.calls import IDEMPOTENT_CALLS, MUTATING_CALLS
    assert "order_send" in MUTATING_CALLS
    assert "order_send" not in IDEMPOTENT_CALLS
    assert not (IDEMPOTENT_CALLS & MUTATING_CALLS)
