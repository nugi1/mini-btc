"""Transaction bytes against an independent implementation (bitcoinjs) and blocks captured from our Core."""

from __future__ import annotations

import pytest

from tests.conftest import load_fixture
from wallet.tx import OutPoint, Transaction, TxIn, TxOut, compact_size

# bitcoinjs-lib "Standard transaction (1:1)", also used by the C# tests (LegacyTransactions.StandardHex).
BITCOINJS_STANDARD_HEX = (
    "0100000001f1fefefefefefefefefefefefefefefefefefefefefefefefefefefefefefefe000000006b"
    "4830450221008732a460737d956fd94d49a31890b2908f7ed7025a9c1d0f25e43290f1841716022004fa7d60"
    "8a291d44ebbbebbadaac18f943031e7de39ef3bf9920998c43e60c0401210279be667ef9dcbbac55a06295ce"
    "870b07029bfcdb2dce28d959f2815b16f81798ffffffff01a086010000000000"
    "1976a914c42e7ef92fdb603af844d064faad95db9bcdfd3d88ac00000000"
)
BITCOINJS_STANDARD_TXID = "a0ff943d3f644d8832b1fa74be4d0ad2577615dc28a7ef74ff8c271b603a082a"


def test_a_legacy_transaction_round_trips_and_hashes_to_its_txid():
    tx = Transaction.parse(BITCOINJS_STANDARD_HEX)

    assert tx.hex() == BITCOINJS_STANDARD_HEX
    assert tx.txid == BITCOINJS_STANDARD_TXID
    assert tx.inputs[0].prevout == OutPoint("fe" * 31 + "f1", 0)  # wire order f1 fe fe ..., displayed reversed
    assert tx.outputs[0].value == 100_000


@pytest.mark.parametrize("block", ["segwit-spend", "mixed"])
def test_every_transaction_in_a_captured_block_round_trips_with_cores_txid_and_wtxid(block):
    from wallet.hashes import hash256

    for entry in load_fixture(block)["tx"]:
        tx = Transaction.parse(entry["hex"])
        assert tx.hex() == entry["hex"]
        assert tx.txid == entry["txid"]
        assert hash256(tx.serialize())[::-1].hex() == entry["hash"]  # Core calls the wtxid "hash"
        assert tx.size == entry["size"]


def test_a_built_transaction_serializes_field_by_field():
    tx = Transaction((TxIn(OutPoint("11" * 32, 1)),), (TxOut(5_000_000_000, bytes.fromhex("51")),))

    assert tx.hex() == (
        "02000000"              # version 2
        "01" + "11" * 32 +      # one input; the txid's bytes are symmetric, so wire order looks the same
        "01000000"              # prevout index 1
        "00"                    # empty scriptSig, to be filled by signing
        "ffffffff"              # final sequence
        "01" "00f2052a01000000"  # one output of 50 BTC = 5,000,000,000 sat = 0x012a05f200
        "01" "51"               # a 1-byte script
        "00000000"              # locktime 0
    )


@pytest.mark.parametrize(("value", "encoded"), [
    (0, "00"), (252, "fc"), (253, "fdfd00"), (0xFFFF, "fdffff"),
    (0x10000, "fe00000100"), (0xFFFFFFFF, "feffffffff"), (0x100000000, "ff0000000001000000"),
])
def test_compact_size_boundaries(value, encoded):
    assert compact_size(value).hex() == encoded


def test_parsing_rejects_trailing_bytes_and_non_canonical_sizes():
    with pytest.raises(ValueError, match="left over"):
        Transaction.parse(BITCOINJS_STANDARD_HEX + "00")
    with pytest.raises(ValueError, match="non-canonical"):
        Transaction.parse("01000000" + "fd0100" + BITCOINJS_STANDARD_HEX[10:])
