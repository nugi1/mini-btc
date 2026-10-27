"""
Base58Check addresses, decoded the way Bitcoin Core v30.0 does (src/key_io.cpp DecodeDestination),
with Core's error messages. A P2PKH address is version byte + HASH160(public key); a P2SH address
is version byte + HASH160(redeem script). The wallet only creates P2PKH, but can pay either.
"""

from __future__ import annotations

from dataclasses import dataclass

from wallet import base58, script
from wallet.network import Network


class AddressError(ValueError):
    """The address can't be used; the message is Core's wording where Core has one."""


@dataclass(frozen=True)
class Address:
    kind: str  # "p2pkh" or "p2sh"
    hash: bytes  # 20 bytes

    @property
    def script_pubkey(self) -> bytes:
        return script.p2pkh(self.hash) if self.kind == "p2pkh" else script.p2sh(self.hash)


def p2pkh_address(pubkey_hash: bytes, network: Network) -> str:
    if len(pubkey_hash) != 20:
        raise ValueError("a pubkey hash is 20 bytes")
    return base58.encode_check(bytes([network.pubkey_address_prefix]) + pubkey_hash)


def decode_address(text: str, network: Network) -> Address:
    if text[: len(network.bech32_hrp)].lower() == network.bech32_hrp:
        # Core would decode this as Bech32. SegWit outputs are out of scope for this wallet.
        raise AddressError("Bech32 (SegWit) addresses are not supported by this wallet; use a Base58 address")

    data = base58.decode_check(text, 21)
    if data is None:
        if base58.decode(text, 100) is None:
            raise AddressError("Invalid or unsupported Segwit (Bech32) or Base58 encoding.")
        raise AddressError("Invalid checksum or length of Base58 address (P2PKH or P2SH)")

    prefixes = {network.pubkey_address_prefix: "p2pkh", network.script_address_prefix: "p2sh"}
    kind = prefixes.get(data[0]) if data else None
    if kind is None:
        raise AddressError("Invalid or unsupported Base58-encoded address.")
    if len(data) != 21:
        raise AddressError("Invalid length for Base58 address (P2PKH or P2SH)")
    return Address(kind, data[1:])
