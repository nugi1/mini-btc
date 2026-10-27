"""
A private key and everything derived from it.

The private key is a number k with 1 <= k < n, where n is the order of secp256k1's generator G.
The public key is the point k*G. Its compressed form is 33 bytes: 02 if y is even, 03 if odd, then x.
The address is Base58Check(version byte || HASH160(compressed public key)). Every step is one-way:
an address reveals neither the public key (until it is spent from) nor the private key.
"""

from __future__ import annotations

import coincurve

from wallet import base58, script
from wallet.address import p2pkh_address
from wallet.hashes import hash160
from wallet.network import Network

# The order of secp256k1's generator G.
CURVE_ORDER = 0xFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEBAAEDCE6AF48A03BBFD25E8CD0364141


class InvalidKey(ValueError):
    """A private key that is out of range or a WIF string that doesn't decode."""


class Key:
    """A secp256k1 private key; this wallet always uses the compressed public key."""

    def __init__(self, secret: bytes) -> None:
        if len(secret) != 32 or not 1 <= int.from_bytes(secret, "big") < CURVE_ORDER:
            raise InvalidKey("a private key is 32 bytes encoding a number in [1, n-1]")
        self._key = coincurve.PrivateKey(secret)

    @classmethod
    def generate(cls) -> Key:
        """32 bytes from the operating system's CSPRNG (coincurve uses os.urandom), retried if out of range."""
        return cls(coincurve.PrivateKey().secret)

    @classmethod
    def from_int(cls, value: int) -> Key:
        return cls(value.to_bytes(32, "big"))

    @classmethod
    def from_wif(cls, wif: str, network: Network) -> Key:
        """
        Core's DecodeSecret: version byte, 32 key bytes, then 01 if the key's public key is used
        compressed. Uncompressed WIFs are refused because this wallet never makes such addresses.
        """
        data = base58.decode_check(wif, 34)
        if data is None or data[0] != network.secret_key_prefix:
            raise InvalidKey("Invalid private key encoding")
        if len(data) == 33:
            raise InvalidKey("uncompressed private keys are not supported by this wallet")
        if len(data) != 34 or data[33] != 0x01:
            raise InvalidKey("Invalid private key encoding")
        return cls(data[1:33])

    @property
    def secret(self) -> bytes:
        return self._key.secret

    @property
    def public_key(self) -> bytes:
        """The 33-byte compressed public key."""
        return self._key.public_key.format(compressed=True)

    @property
    def pubkey_hash(self) -> bytes:
        return hash160(self.public_key)

    @property
    def script_pubkey(self) -> bytes:
        return script.p2pkh(self.pubkey_hash)

    def address(self, network: Network) -> str:
        return p2pkh_address(self.pubkey_hash, network)

    def wif(self, network: Network) -> str:
        """Wallet Import Format: Base58Check(version || key || 01), the trailing 01 meaning compressed."""
        return base58.encode_check(bytes([network.secret_key_prefix]) + self.secret + b"\x01")

    def sign(self, digest: bytes) -> bytes:
        """
        ECDSA over a 32-byte digest, DER-encoded. libsecp256k1 derives the nonce with RFC 6979
        (deterministic, so signing the same digest twice gives the same bytes) and always returns
        low-S, which Core's standardness rules require (BIP 62 / SCRIPT_VERIFY_LOW_S).
        """
        if len(digest) != 32:
            raise ValueError("sign a 32-byte digest")
        return self._key.sign(digest, hasher=None)
