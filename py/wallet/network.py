"""Chain parameters the wallet needs (Core: CRegTestParams in src/kernel/chainparams.cpp)."""

from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True)
class Network:
    name: str
    pubkey_address_prefix: int  # P2PKH addresses
    script_address_prefix: int  # P2SH addresses
    secret_key_prefix: int      # WIF private keys
    bech32_hrp: str


# 111 = 0x6f gives addresses starting with m or n; 196 = 0xc4 starts with 2; 239 = 0xef starts with c
# (compressed key) or 9 (uncompressed). Testnet uses the same three bytes, so these strings are
# valid there too.
REGTEST = Network("regtest", 0x6F, 0xC4, 0xEF, "bcrt")
