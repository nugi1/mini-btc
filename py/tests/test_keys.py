"""
Keys, hashes and addresses against Bitcoin Core's own test vectors
(src/test/data/*.json in Core v30.0, copied into tests/data) and against the node's C# results.
"""

from __future__ import annotations

import json
from pathlib import Path

import coincurve
import pytest

from wallet import base58
from wallet.address import AddressError, decode_address, p2pkh_address
from wallet.hashes import hash160, ripemd160
from wallet.keys import CURVE_ORDER, InvalidKey, Key
from wallet.network import REGTEST

DATA = Path(__file__).parent / "data"

# Private key 1: its public key is the generator G itself. The C# tests and the node's smoke test
# use the same key, so these three strings are shared ground truth across both languages.
KEY_ONE_PUBKEY = "0279be667ef9dcbbac55a06295ce870b07029bfcdb2dce28d959f2815b16f81798"
KEY_ONE_ADDRESS = "mrCDrCybB6J1vRfbwM5hemdJz73FwDBC8r"
KEY_ONE_WIF = "cMahea7zqjxrtgAbB7LSGbcQUr1uX1ojuat9jZodMN87JcbXMTcA"


@pytest.mark.parametrize(("message", "digest"), [
    (b"", "9c1185a5c5e9fc54612808977ee8f548b2258d31"),
    (b"abc", "8eb208f7e05d987a9b044a8e98c6b087f15a0bfc"),
    (b"message digest", "5d0689ef49d2fae572b881b123a85ffa21595f36"),
    (b"1234567890" * 8, "9b752e45573d4b39f4dbd3323cab82bf63326bfb"),
    (b"a" * 1_000_000, "52783243c1697bdbe16d37f97f68f08325dc1528"),
])
def test_ripemd160_matches_the_reference_vectors(message, digest):
    assert ripemd160(message).hex() == digest


@pytest.mark.parametrize(("hex_data", "text"), json.loads((DATA / "base58_encode_decode.json").read_text()))
def test_base58_matches_cores_vectors(hex_data, text):
    data = bytes.fromhex(hex_data)
    assert base58.encode(data) == text
    assert base58.decode(text, 256) == data


def test_base58_decode_rejects_what_core_rejects():
    assert base58.decode("invalid0", 100) is None  # 0 is not in the alphabet
    assert base58.decode(" \t\n\v\f\r skip \r\f\v\n\t ", 100) == bytes.fromhex("971a55")  # Core skips outer spaces
    assert base58.decode("3vQB 7B6MrGQZaxCuFg4oh", 100) is None  # but not inner ones
    assert base58.decode("1111", 3) is None  # four zero bytes don't fit in three


def test_key_one_derives_g_its_hash_address_and_wif():
    key = Key.from_int(1)

    assert key.public_key.hex() == KEY_ONE_PUBKEY
    assert key.pubkey_hash.hex() == "751e76e8199196d454941c45d1b3a323f1433bd6"
    assert key.address(REGTEST) == KEY_ONE_ADDRESS
    assert key.wif(REGTEST) == KEY_ONE_WIF
    assert key.script_pubkey.hex() == "76a914751e76e8199196d454941c45d1b3a323f1433bd688ac"


def test_wif_round_trips():
    key = Key.generate()

    assert Key.from_wif(key.wif(REGTEST), REGTEST).secret == key.secret


@pytest.mark.parametrize("vector", [v for v in json.loads((DATA / "key_io_valid_regtest.json").read_text())
                                    if v[2]["isPrivkey"]])
def test_cores_regtest_wifs(vector):
    wif, secret_hex, meta = vector
    if not meta["isCompressed"]:
        with pytest.raises(InvalidKey, match="uncompressed"):
            Key.from_wif(wif, REGTEST)
        return
    key = Key.from_wif(wif, REGTEST)
    assert key.secret.hex() == secret_hex
    assert key.wif(REGTEST) == wif


@pytest.mark.parametrize("vector", [v for v in json.loads((DATA / "key_io_valid_regtest.json").read_text())
                                    if not v[2]["isPrivkey"] and not v[0].startswith("bcrt")])
def test_cores_regtest_base58_addresses_decode_to_their_scripts(vector):
    address, script_hex, _ = vector
    assert decode_address(address, REGTEST).script_pubkey.hex() == script_hex


@pytest.mark.parametrize("vector", json.loads((DATA / "key_io_invalid.json").read_text()))
def test_cores_invalid_strings_are_neither_addresses_nor_keys(vector):
    text = vector[0]
    with pytest.raises(AddressError):
        decode_address(text, REGTEST)
    with pytest.raises(InvalidKey):
        Key.from_wif(text, REGTEST)


def test_address_errors_use_cores_messages():
    mainnet_key_one = base58.encode_check(b"\x00" + bytes.fromhex("751e76e8199196d454941c45d1b3a323f1433bd6"))
    typo = KEY_ONE_ADDRESS[:-1] + "s"
    too_long = base58.encode_check(b"\x6f" + bytes(21))

    with pytest.raises(AddressError, match=r"^Invalid or unsupported Base58-encoded address\.$"):
        decode_address(mainnet_key_one, REGTEST)
    with pytest.raises(AddressError, match=r"^Invalid checksum or length of Base58 address \(P2PKH or P2SH\)$"):
        decode_address(typo, REGTEST)
    with pytest.raises(AddressError, match=r"^Invalid checksum or length of Base58 address \(P2PKH or P2SH\)$"):
        decode_address(too_long, REGTEST)  # 22 bytes is over DecodeBase58Check's limit of 21
    with pytest.raises(AddressError, match=r"^Invalid or unsupported Segwit \(Bech32\) or Base58 encoding\.$"):
        decode_address("mrCDrCybB6J1vRfbwM5hemdJz73FwDBC80", REGTEST)
    with pytest.raises(AddressError, match="Bech32"):
        decode_address("bcrt1q550ppjnxunjt03e50jjqqxkn34u3yug730el6g", REGTEST)


def test_a_short_payload_with_a_valid_prefix_is_a_length_error():
    with pytest.raises(AddressError, match=r"^Invalid length for Base58 address \(P2PKH or P2SH\)$"):
        decode_address(base58.encode_check(b"\x6f" + bytes(19)), REGTEST)


def test_p2pkh_address_is_the_inverse_of_decoding():
    assert p2pkh_address(decode_address(KEY_ONE_ADDRESS, REGTEST).hash, REGTEST) == KEY_ONE_ADDRESS


def test_private_keys_outside_1_to_n_minus_1_are_refused():
    for value in (0, CURVE_ORDER):
        with pytest.raises(InvalidKey):
            Key(value.to_bytes(32, "big"))
    Key.from_int(CURVE_ORDER - 1)


def test_generated_keys_are_fresh_regtest_p2pkh_keys():
    first, second = Key.generate(), Key.generate()

    assert first.secret != second.secret
    assert first.address(REGTEST)[0] in "mn"
    assert hash160(first.public_key) == decode_address(first.address(REGTEST), REGTEST).hash


def test_signatures_are_deterministic_low_s_and_verify():
    key, digest = Key.from_int(1), bytes(range(32))

    signature = key.sign(digest)

    assert signature == key.sign(digest)  # RFC 6979
    assert coincurve.PublicKey(key.public_key).verify(signature, digest, hasher=None)
    s = _der_s(signature)
    assert s <= CURVE_ORDER // 2


def _der_s(signature: bytes) -> int:
    r_length = signature[3]
    s_offset = 4 + r_length
    return int.from_bytes(signature[s_offset + 2: s_offset + 2 + signature[s_offset + 1]], "big")
