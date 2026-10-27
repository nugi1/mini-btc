"""Bitcoin's composite hashes (Core: Hash and Hash160 in src/hash.h)."""

from __future__ import annotations

import hashlib

from wallet._ripemd160 import ripemd160 as _pure_ripemd160


def sha256(data: bytes) -> bytes:
    return hashlib.sha256(data).digest()


def hash256(data: bytes) -> bytes:
    """SHA-256 twice: txids, block hashes, the Base58Check checksum, the legacy signature hash."""
    return sha256(sha256(data))


def ripemd160(data: bytes) -> bytes:
    return _pure_ripemd160(data)


def hash160(data: bytes) -> bytes:
    """RIPEMD-160(SHA-256(data)), 20 bytes: what a P2PKH address commits to."""
    return ripemd160(sha256(data))
