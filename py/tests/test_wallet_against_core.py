"""
E3's acceptance test: a wallet-built, wallet-signed transaction passes Core's testmempoolaccept.

It runs the real path: Core mines 101 blocks to a fresh key, the node syncs them, the wallet asks
the node for its coins, builds and signs a payment, and Core judges it. testmempoolaccept runs
every mempool check (consensus and standardness) without adding the transaction, so the test
leaves only the mined blocks behind.

Needs `docker compose up -d --wait` and the node:
    dotnet run --project src/MiniBtc.Api --urls http://127.0.0.1:5080
"""

from __future__ import annotations

import time
from dataclasses import replace
from decimal import Decimal

import pytest

from wallet.builder import build_payment
from wallet.keys import Key
from wallet.network import REGTEST
from wallet.node_client import NodeClient, NodeError
from wallet.signing import sign_p2pkh

pytestmark = pytest.mark.integration

BTC = 100_000_000


@pytest.fixture(scope="module")
def node(node_url) -> NodeClient:
    client = NodeClient(node_url)
    try:
        client.balance(Key.from_int(1).address(REGTEST))
    except NodeError as error:
        pytest.fail(f"The node is not reachable ({error}). Start it with "
                    "`dotnet run --project src/MiniBtc.Api --urls http://127.0.0.1:5080`.")
    return client


@pytest.fixture(scope="module")
def alice(core, node):
    """A fresh key with 101 coinbases, once the node has caught up with Core."""
    key = Key.generate()
    core.generatetoaddress(101, key.address(REGTEST))
    target = core.getblockcount()
    deadline = time.monotonic() + 30
    while True:
        tip, coins = node.utxos(key.address(REGTEST))
        if tip >= target:
            return key, tip, coins
        if time.monotonic() > deadline:
            pytest.fail(f"the node is at height {tip}, Core at {target}, after 30 s")
        time.sleep(0.2)


def test_the_node_sees_the_same_coins_as_core(core, alice):
    key, tip, coins = alice

    scan = core.scantxoutset("start", [f"addr({key.address(REGTEST)})"])

    assert scan["height"] == tip
    assert sorted((u["txid"], u["vout"]) for u in scan["unspents"]) == sorted(
        (c.outpoint.txid, c.outpoint.vout) for c in coins)
    assert int(scan["total_amount"] * BTC) == sum(c.value for c in coins)
    assert len(coins) == 101


def test_exactly_the_coinbases_at_least_100_deep_in_the_next_block_are_spendable(alice):
    _, tip, coins = alice

    assert sorted(c.height for c in coins if c.spendable) == [tip - 100, tip - 99]


def test_a_wallet_signed_payment_passes_testmempoolaccept(core, alice):
    key, _, coins = alice
    bob = Key.generate()

    payment = build_payment(coins, bob.script_pubkey, 10 * BTC, key.script_pubkey)
    signed = sign_p2pkh(payment.tx, [c.script_pubkey for c in payment.spent], key)

    verdict = core.testmempoolaccept([signed.hex()])[0]
    assert verdict["allowed"], verdict.get("reject-reason")
    assert verdict["txid"] == signed.txid
    assert verdict["vsize"] == signed.size
    assert int(verdict["fees"]["base"] * BTC) == payment.fee
    assert Decimal(payment.fee) / signed.size >= 1  # at least the requested 1 sat/vB


def test_the_youngest_spendable_coinbase_is_accepted_and_the_next_one_is_premature(core, alice):
    """The node's spendable flag against Core's own maturity check, at the exact boundary."""
    key, tip, coins = alice
    youngest_mature = next(c for c in coins if c.height == tip - 99)
    oldest_immature = next(c for c in coins if c.height == tip - 98)
    assert youngest_mature.spendable and not oldest_immature.spendable

    def verdict(coin):
        # Bypass the wallet's own check, so Core is the one that judges maturity.
        payment = build_payment([replace(coin, spendable=True)], key.script_pubkey, 1 * BTC, key.script_pubkey)
        return core.testmempoolaccept([sign_p2pkh(payment.tx, [coin.script_pubkey], key).hex()])[0]

    assert verdict(youngest_mature)["allowed"]
    rejected = verdict(oldest_immature)
    assert not rejected["allowed"]
    assert rejected["reject-reason"] == "bad-txns-premature-spend-of-coinbase"
