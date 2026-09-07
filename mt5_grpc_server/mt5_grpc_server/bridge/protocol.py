"""Wire format shared by the two halves of the MT5 bridge.

One message is a 4-byte big-endian length header followed by UTF-8 JSON. Both
sides hand the socket at most ``FRAME_BYTES`` at a time.

On the slicing: the large payloads travel from the Wine side to the Linux side,
and an oversized single write is what breaks grpcio's Windows endpoint under
Wine - it aborts the process when a send completes partially instead of writing
the remainder. Plain Python sockets get this right (``sendall`` loops), so the
slicing here is belt and braces rather than the load-bearing part; it also
bounds how much has to sit in one buffer.
"""
import json
import math
import os
import struct

FRAME_BYTES = int(os.environ.get("BRIDGE_FRAME_BYTES", "32768"))
MAX_MESSAGE_BYTES = int(os.environ.get("BRIDGE_MAX_MESSAGE_BYTES", str(64 * 1024 * 1024)))

_HEADER = struct.Struct(">I")


class ProtocolError(Exception):
    """The peer sent something that is not a well-formed bridge message."""


def to_jsonable(value):
    """Convert an MT5 return value into something ``json`` can encode.

    MT5 hands back namedtuples, and tuples of them. NaN and infinity become
    ``None``: ``json.dumps`` would otherwise emit a bare ``NaN``, which is not
    valid JSON and would break any non-Python reader added later.
    """
    if value is None or isinstance(value, (bool, int, str)):
        return value
    if isinstance(value, float):
        return None if (math.isnan(value) or math.isinf(value)) else value
    if hasattr(value, "_asdict"):
        return {key: to_jsonable(val) for key, val in value._asdict().items()}
    if isinstance(value, dict):
        return {str(key): to_jsonable(val) for key, val in value.items()}
    if isinstance(value, (list, tuple)):
        return [to_jsonable(item) for item in value]
    if isinstance(value, (bytes, bytearray)):
        return value.decode("utf-8", "replace")
    return str(value)


def encode(message):
    payload = json.dumps(message, allow_nan=False, separators=(",", ":")).encode("utf-8")
    return _HEADER.pack(len(payload)) + payload


def send(sock, message):
    """Write one message, never handing the socket more than FRAME_BYTES."""
    blob = encode(message)
    for start in range(0, len(blob), FRAME_BYTES):
        sock.sendall(blob[start:start + FRAME_BYTES])


def _read_exactly(sock, count):
    chunks = []
    remaining = count
    while remaining:
        chunk = sock.recv(min(remaining, FRAME_BYTES))
        if not chunk:
            raise ConnectionError("peer closed the bridge connection")
        chunks.append(chunk)
        remaining -= len(chunk)
    return b"".join(chunks)


def recv(sock):
    """Read one message. Raises ConnectionError when the peer goes away."""
    length = _HEADER.unpack(_read_exactly(sock, _HEADER.size))[0]
    if length > MAX_MESSAGE_BYTES:
        raise ProtocolError(
            "message of %d bytes exceeds the %d byte limit" % (length, MAX_MESSAGE_BYTES))
    return json.loads(_read_exactly(sock, length).decode("utf-8"))
