"""Make the repo's two packages importable when the suite is run as a directory.

Both live one level down from the repo root (``mt5_grpc_proto/mt5_grpc_proto``
and ``mt5_grpc_server/mt5_grpc_server``), and the outer directories share their
names. With the repo root on ``sys.path`` first, ``import mt5_grpc_server``
resolves to the outer directory - which has no submodules - so collection fails
with "No module named mt5_grpc_server.imp". Putting the package parents ahead of
the repo root fixes it for every test file at once.
"""
import os
import sys

_REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

for _name in ("mt5_grpc_proto", "mt5_grpc_server"):
    _path = os.path.join(_REPO_ROOT, _name)
    if _path in sys.path:
        sys.path.remove(_path)
    sys.path.insert(0, _path)

if _REPO_ROOT in sys.path:
    sys.path.remove(_REPO_ROOT)
    sys.path.append(_REPO_ROOT)
