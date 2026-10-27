"""The wallet CLI end to end, against a fake node and a fake Core."""

from __future__ import annotations

import argparse
import stat

import coincurve
import pytest

from wallet.cli import main, parse_btc
from wallet.keys import Key
from wallet.network import REGTEST
from wallet.signing import legacy_sighash_all
from wallet.tx import Transaction

BTC = 100_000_000
KEY_ONE = Key.from_int(1)
BOB = Key.from_int(2).address(REGTEST)
FUNDING_TXID = "aa" * 32


@pytest.fixture
def wallet_dir(tmp_path, monkeypatch):
    monkeypatch.setenv("MINIBTC_WALLET_DIR", str(tmp_path))
    return tmp_path


@pytest.fixture
def node(fake_http, monkeypatch):
    """A node at tip 101 holding one mature and one immature 50 BTC coinbase for key one."""
    def route(method, path, headers, body):
        address = KEY_ONE.address(REGTEST)
        if path == f"/addresses/{address}/utxos":
            return 200, {
                "address": address, "scriptPubKey": KEY_ONE.script_pubkey.hex(), "tipHeight": 101,
                "utxos": [
                    {"txid": FUNDING_TXID, "vout": 0, "value": 50 * BTC, "height": 1, "confirmations": 101,
                     "coinbase": True, "spendable": True, "maturityHeight": 101},
                    {"txid": "bb" * 32, "vout": 0, "value": 50 * BTC, "height": 101, "confirmations": 1,
                     "coinbase": True, "spendable": False, "maturityHeight": 201},
                ],
            }
        if path == f"/addresses/{address}/balance":
            return 200, {"address": address, "tipHeight": 101, "spendable": 50 * BTC, "immature": 50 * BTC,
                         "total": 100 * BTC, "utxoCount": 2}
        if method == "POST" and path == "/tx":
            if server.reject:
                return 400, server.reject
            return 200, {"txid": Transaction.parse(body["hex"]).txid, "fee": 226, "unverifiedInputs": 0}
        return 400, {"error": "Invalid or unsupported Base58-encoded address."}

    server = fake_http(route)
    server.reject = None  # set to a 400 body to make POST /tx refuse
    monkeypatch.setenv("MINIBTC_NODE_URL", server.url)
    return server


@pytest.fixture
def core(fake_http, monkeypatch):
    def route(method, path, headers, body):
        if body["method"] == "testmempoolaccept":
            txid = Transaction.parse(body["params"][0][0]).txid
            return 200, {"jsonrpc": "2.0", "id": body["id"], "result": [{"txid": txid, "allowed": True, "vsize": 225}]}
        return 200, {"jsonrpc": "2.0", "id": body["id"], "result": Transaction.parse(body["params"][0]).txid}

    server = fake_http(route)
    monkeypatch.setenv("MINIBTC_CORE_RPC_URL", server.url)
    return server


def test_new_creates_a_private_wallet_file_and_prints_the_address(wallet_dir, capsys):
    assert main(["--wallet", "alice", "new"]) == 0

    address = capsys.readouterr().out.strip()
    path = wallet_dir / "alice.json"
    assert address[0] in "mn"
    assert stat.S_IMODE(path.stat().st_mode) == 0o600
    assert main(["--wallet", "alice", "address"]) == 0
    assert capsys.readouterr().out.strip() == address


def test_new_never_overwrites_an_existing_key(wallet_dir, capsys):
    main(["--wallet", "alice", "new"])

    assert main(["--wallet", "alice", "new"]) == 1
    assert "refusing to overwrite" in capsys.readouterr().err


def test_balance_comes_from_the_node(wallet_dir, node, capsys):
    main(["--wallet", "alice", "new", "--wif", KEY_ONE.wif(REGTEST)])
    capsys.readouterr()

    assert main(["--wallet", "alice", "balance"]) == 0

    out = capsys.readouterr().out
    assert "spendable  50.00000000 BTC" in out
    assert "immature   50.00000000 BTC" in out


def test_send_spends_only_the_mature_coin_signs_it_and_asks_core(wallet_dir, node, core, capsys):
    main(["--wallet", "alice", "new", "--wif", KEY_ONE.wif(REGTEST)])

    assert main(["--wallet", "alice", "send", "--to", BOB, "--amount", "10", "--dry-run"]) == 0

    sent = Transaction.parse(core.requests[-1][3]["params"][0][0])
    assert [i.prevout.txid for i in sent.inputs] == [FUNDING_TXID]
    assert [o.value for o in sent.outputs] == [10 * BTC, 40 * BTC - 226]
    script_sig = sent.inputs[0].script_sig
    unsigned = sent.with_script_sig(0, b"")
    digest = legacy_sighash_all(unsigned, 0, KEY_ONE.script_pubkey)
    assert coincurve.PublicKey(KEY_ONE.public_key).verify(script_sig[1: script_sig[0]], digest, hasher=None)
    assert "Core would accept it" in capsys.readouterr().out


def test_send_goes_through_the_nodes_gateway_and_prints_the_txid(wallet_dir, node, core, capsys):
    main(["--wallet", "alice", "new", "--wif", KEY_ONE.wif(REGTEST)])
    capsys.readouterr()

    assert main(["--wallet", "alice", "send", "--to", BOB, "--amount", "1.5"]) == 0

    method, path, _, body = node.requests[-1]
    assert (method, path) == ("POST", "/tx")
    assert capsys.readouterr().out.strip() == Transaction.parse(body["hex"]).txid
    assert core.requests == []


def test_send_direct_skips_the_node_and_goes_to_core(wallet_dir, node, core, capsys):
    main(["--wallet", "alice", "new", "--wif", KEY_ONE.wif(REGTEST)])
    capsys.readouterr()

    assert main(["--wallet", "alice", "send", "--to", BOB, "--amount", "1.5", "--direct"]) == 0

    sent = Transaction.parse(core.requests[-1][3]["params"][0])
    assert capsys.readouterr().out.strip() == sent.txid
    assert core.requests[-1][3]["method"] == "sendrawtransaction"
    assert all(path != "/tx" for _, path, _, _ in node.requests)


def test_a_rejection_says_who_rejected_it_and_why(wallet_dir, node, core, capsys):
    main(["--wallet", "alice", "new", "--wif", KEY_ONE.wif(REGTEST)])
    node.reject = {"rejectedBy": "node", "code": "bip125-replacement-disallowed",
                   "error": "already spent by mempool transaction cc…"}

    assert main(["--wallet", "alice", "send", "--to", BOB, "--amount", "1"]) == 1

    assert "rejected by node: bip125-replacement-disallowed" in capsys.readouterr().err


def test_more_than_the_spendable_balance_fails_before_anything_is_signed(wallet_dir, node, core, capsys):
    main(["--wallet", "alice", "new", "--wif", KEY_ONE.wif(REGTEST)])

    assert main(["--wallet", "alice", "send", "--to", BOB, "--amount", "60"]) == 1

    assert "immature" in capsys.readouterr().err
    assert core.requests == []


def test_a_bad_destination_is_refused_with_cores_message(wallet_dir, node, core, capsys):
    main(["--wallet", "alice", "new", "--wif", KEY_ONE.wif(REGTEST)])

    assert main(["--wallet", "alice", "send", "--to", BOB[:-1] + "x", "--amount", "1"]) == 1

    assert "Invalid checksum or length of Base58 address" in capsys.readouterr().err


@pytest.mark.parametrize(("text", "sats"), [("10", 10 * BTC), ("0.1", 10_000_000), ("0.00000546", 546),
                                             ("21000000", 21_000_000 * BTC)])
def test_amounts_convert_to_satoshis_exactly(text, sats):
    assert parse_btc(text) == sats


@pytest.mark.parametrize("text", ["0.000000001", "-1", "0", "nan", "ten"])
def test_amounts_that_are_not_whole_satoshis_are_refused(text):
    with pytest.raises(argparse.ArgumentTypeError):
        parse_btc(text)
