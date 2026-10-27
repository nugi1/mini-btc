"""
Signing, checked against independent implementations byte for byte: bitcoinjs-lib's signed
transaction, Core's wallet's signature in a captured block, and the node's C# sighash.
"""

from __future__ import annotations

import coincurve
import pytest

from tests.conftest import load_fixture
from tests.test_tx import BITCOINJS_STANDARD_HEX, BITCOINJS_STANDARD_TXID
from wallet.keys import Key
from wallet.signing import SIGHASH_ALL, is_low_s, legacy_sighash_all, sign_p2pkh
from wallet.tx import OutPoint, Transaction, TxIn


def _core_wallet_spend() -> tuple[Transaction, bytes]:
    """The legacy P2PKH spend in the captured 'mixed' block, and the script of the output it spends."""
    mixed = [Transaction.parse(t["hex"]) for t in load_fixture("mixed")["tx"]]
    tx = next(t for t in mixed[1:] if not t.has_witness)
    funding = next(t for t in load_fixture("segwit-spend")["tx"] if t["txid"] == tx.inputs[0].prevout.txid)
    spent = bytes.fromhex(funding["vout"][tx.inputs[0].prevout.vout]["scriptPubKey"]["hex"])
    return tx, spent


def test_sighash_of_cores_spend_matches_the_csharp_node():
    tx, spent = _core_wallet_spend()

    # The same value P2pkhVerifierTests asserts for LegacySignatureHash.ComputeAll in C#.
    assert legacy_sighash_all(tx, 0, spent).hex() == "fc858670084c7b03878be6f2f89b7abba202d3d8567c094e7a75bee1fd643189"


def test_cores_signature_verifies_against_our_sighash():
    tx, spent = _core_wallet_spend()
    sig_push, pubkey_push = tx.inputs[0].script_sig[0], tx.inputs[0].script_sig[1 + tx.inputs[0].script_sig[0]]
    signature = tx.inputs[0].script_sig[1: sig_push]  # without the trailing hash type byte
    pubkey = tx.inputs[0].script_sig[2 + sig_push: 2 + sig_push + pubkey_push]

    assert tx.inputs[0].script_sig[sig_push] == SIGHASH_ALL
    assert coincurve.PublicKey(pubkey).verify(signature, legacy_sighash_all(tx, 0, spent), hasher=None)


def test_signing_bitcoinjs_unsigned_tx_with_key_one_reproduces_its_bytes_exactly():
    # Both libraries derive the nonce with RFC 6979, so the same key and message give the same signature.
    signed_by_bitcoinjs = Transaction.parse(BITCOINJS_STANDARD_HEX)
    unsigned = signed_by_bitcoinjs.with_script_sig(0, b"")
    key = Key.from_int(1)

    signed = sign_p2pkh(unsigned, [key.script_pubkey], key)

    assert signed.hex() == BITCOINJS_STANDARD_HEX
    assert signed.txid == BITCOINJS_STANDARD_TXID


def test_every_input_gets_its_own_signature_over_the_whole_transaction():
    key = Key.from_int(1)
    tx = Transaction.parse(BITCOINJS_STANDARD_HEX).with_script_sig(0, b"")
    two_inputs = Transaction(tx.inputs + (TxIn(OutPoint("ab" * 32, 3)),), tx.outputs)

    signed = sign_p2pkh(two_inputs, [key.script_pubkey] * 2, key)

    for index in range(2):
        script_sig = signed.inputs[index].script_sig
        signature = script_sig[1: script_sig[0]]
        digest = legacy_sighash_all(two_inputs, index, key.script_pubkey)
        assert coincurve.PublicKey(key.public_key).verify(signature, digest, hasher=None)
        assert is_low_s(signature)
        assert script_sig[script_sig[0] + 1:] == bytes([33]) + key.public_key
    assert signed.inputs[0].script_sig != signed.inputs[1].script_sig


def test_a_coin_of_another_key_is_refused():
    tx = Transaction.parse(BITCOINJS_STANDARD_HEX).with_script_sig(0, b"")

    with pytest.raises(ValueError, match="can't sign"):
        sign_p2pkh(tx, [Key.from_int(2).script_pubkey], Key.from_int(1))


def test_is_low_s_tells_the_two_halves_apart():
    assert is_low_s(bytes.fromhex("3007020101020200ff"))  # s = 255
    n_minus_1 = (0xFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEBAAEDCE6AF48A03BBFD25E8CD0364140).to_bytes(32, "big")
    assert not is_low_s(bytes([0x30, 6 + 33, 0x02, 0x01, 0x01, 0x02, 33]) + b"\x00" + n_minus_1)
