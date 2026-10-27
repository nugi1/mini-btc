"""
Legacy (pre-SegWit) signing of P2PKH inputs with SIGHASH_ALL.

A signature can't cover itself, so for each input the signer hashes a modified copy of the
transaction (Core: SignatureHash with SigVersion::BASE, src/script/interpreter.cpp):

    every input's scriptSig emptied, except the one being signed, which holds the script of the
    output it spends (the "scriptCode"); then the 4-byte hash type 01 00 00 00 appended;
    then HASH256 of all of that.

SIGHASH_ALL commits to every input and every output, so changing any amount or address breaks
every signature. The finished scriptSig is <DER signature + 01> <compressed public key>: the
P2PKH script checks HASH160(public key) against the address, then the signature against the key.
"""

from __future__ import annotations

import struct

from wallet import script
from wallet.hashes import hash256
from wallet.keys import CURVE_ORDER, Key
from wallet.tx import Transaction, compact_size, var_bytes

SIGHASH_ALL = 0x01


def legacy_sighash_all(tx: Transaction, input_index: int, script_code: bytes) -> bytes:
    """The 32-byte digest an ECDSA signature for this input signs (wire order, not reversed)."""
    if not 0 <= input_index < len(tx.inputs):
        raise IndexError("input index out of range")
    preimage = struct.pack("<I", tx.version) + compact_size(len(tx.inputs))
    for index, txin in enumerate(tx.inputs):
        preimage += txin.prevout.serialize()
        preimage += var_bytes(script_code) if index == input_index else b"\x00"
        preimage += struct.pack("<I", txin.sequence)
    preimage += compact_size(len(tx.outputs)) + b"".join(o.serialize() for o in tx.outputs)
    preimage += struct.pack("<I", tx.locktime) + struct.pack("<I", SIGHASH_ALL)
    return hash256(preimage)


def sign_p2pkh(tx: Transaction, spent_scripts: list[bytes], key: Key) -> Transaction:
    """
    Signs every input. spent_scripts[i] is the scriptPubKey of the output input i spends; each
    must be this key's P2PKH script. Inputs are signed against the unsigned transaction: other
    inputs' scriptSigs are emptied in the sighash anyway, so the order of signing doesn't matter.
    """
    if len(spent_scripts) != len(tx.inputs):
        raise ValueError("one spent script per input")
    signed = tx
    for index, spent in enumerate(spent_scripts):
        if spent != key.script_pubkey:
            raise ValueError(f"input {index} spends a script this key can't sign: {spent.hex()}")
        signature = key.sign(legacy_sighash_all(tx, index, spent))
        if not is_low_s(signature):  # libsecp256k1 guarantees this; Core's mempool requires it
            raise AssertionError("signer returned a high-S signature")
        script_sig = script.push(signature + bytes([SIGHASH_ALL])) + script.push(key.public_key)
        signed = signed.with_script_sig(index, script_sig)
    return signed


def is_low_s(der: bytes) -> bool:
    """S at most n/2. Both (r, s) and (r, n-s) verify, so Core's policy accepts only the low one
    to stop third parties from changing a txid by flipping s (BIP 62, SCRIPT_VERIFY_LOW_S)."""
    r_length = der[3]
    s_offset = 4 + r_length
    s = int.from_bytes(der[s_offset + 2: s_offset + 2 + der[s_offset + 1]], "big")
    return s <= CURVE_ORDER // 2
