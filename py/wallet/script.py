"""The output scripts the wallet pays to, and the one push encoding a P2PKH scriptSig needs."""

from __future__ import annotations

OP_DUP = 0x76
OP_HASH160 = 0xA9
OP_EQUAL = 0x87
OP_EQUALVERIFY = 0x88
OP_CHECKSIG = 0xAC


def p2pkh(pubkey_hash: bytes) -> bytes:
    """OP_DUP OP_HASH160 <20 bytes> OP_EQUALVERIFY OP_CHECKSIG: 76 a9 14 <hash> 88 ac, 25 bytes."""
    if len(pubkey_hash) != 20:
        raise ValueError("a pubkey hash is 20 bytes")
    return bytes([OP_DUP, OP_HASH160, 20]) + pubkey_hash + bytes([OP_EQUALVERIFY, OP_CHECKSIG])


def p2sh(script_hash: bytes) -> bytes:
    """OP_HASH160 <20 bytes> OP_EQUAL: a9 14 <hash> 87, 23 bytes."""
    if len(script_hash) != 20:
        raise ValueError("a script hash is 20 bytes")
    return bytes([OP_HASH160, 20]) + script_hash + bytes([OP_EQUAL])


def push(data: bytes) -> bytes:
    """
    The minimal push for 1..75 bytes: one length byte, which is itself the opcode. A signature
    (at most 73 bytes with its hash type) and a compressed public key (33) always fit, so the
    OP_PUSHDATA1/2/4 forms are never needed here.
    """
    if not 1 <= len(data) <= 75:
        raise ValueError("only 1-75 byte pushes are used by P2PKH")
    return bytes([len(data)]) + data
