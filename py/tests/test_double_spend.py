"""
E4's double-spend acceptance test, through the real node and the real Core.

Alice pays Bob through the node's gateway. Then she signs a second transaction spending the same coin
to Carol, at the same fee rate:
  - the node refuses it itself, before Core sees it: the coin is already spent in its mempool view,
    and this node never replaces (Core's reason for that: bip125-replacement-disallowed);
  - sent straight to Core, Core refuses it too. Core v30 treats every conflict as a replacement
    attempt (full RBF), and a replacement must pay a higher fee rate: "insufficient fee".
After a block confirms the payment, the same double spend is refused by both for the stronger reason:
the coin no longer exists (bad-txns-inputs-missingorspent).

Needs `docker compose up -d --wait` and the node:
    dotnet run --project src/MiniBtc.Api --urls http://127.0.0.1:5080
"""

from __future__ import annotations

import time

import pytest

from btc_rpc import CoreRpcError, RpcErrorCode
from wallet.builder import Coin, build_payment
from wallet.keys import Key
from wallet.network import REGTEST
from wallet.node_client import NodeClient, NodeError, Rejected
from wallet.signing import sign_p2pkh

pytestmark = pytest.mark.integration

BTC = 100_000_000


def wait_until(condition, what: str, timeout: float = 30) -> None:
    deadline = time.monotonic() + timeout
    while not condition():
        if time.monotonic() > deadline:
            pytest.fail(f"timed out after {timeout} s waiting for {what}")
        time.sleep(0.2)


@pytest.fixture(scope="module")
def node(node_url) -> NodeClient:
    client = NodeClient(node_url)
    try:
        client.mempool()
    except NodeError as error:
        pytest.fail(f"The node is not reachable ({error}). Start it with "
                    "`dotnet run --project src/MiniBtc.Api --urls http://127.0.0.1:5080`.")
    return client


def pay(key: Key, coin: Coin, to: Key, amount: int) -> str:
    payment = build_payment([coin], to.script_pubkey, amount, key.script_pubkey)
    return sign_p2pkh(payment.tx, [coin.script_pubkey], key).hex()


def test_a_double_spend_is_refused_by_the_node_and_by_core(core, node):
    alice, bob, carol = Key.generate(), Key.generate(), Key.generate()
    core.generatetoaddress(101, alice.address(REGTEST))
    target = core.getblockcount()
    wait_until(lambda: node.utxos(alice.address(REGTEST))[0] >= target, "the node to sync the 101 blocks")
    coin = next(c for c in node.utxos(alice.address(REGTEST))[1] if c.spendable)

    to_bob = pay(alice, coin, bob, 10 * BTC)
    txid = node.broadcast(to_bob)
    assert core.getmempoolentry(txid)["vsize"] > 0
    wait_until(lambda: txid in node.mempool(), "the payment in the node's mempool view")

    to_carol = pay(alice, coin, carol, 10 * BTC)

    with pytest.raises(Rejected) as by_node:
        node.broadcast(to_carol)
    assert (by_node.value.rejected_by, by_node.value.code) == ("node", "bip125-replacement-disallowed")

    with pytest.raises(CoreRpcError) as by_core:
        core.sendrawtransaction(to_carol)
    assert by_core.value.code == RpcErrorCode.VERIFY_REJECTED
    assert by_core.value.message.startswith("insufficient fee")

    core.generatetoaddress(1, alice.address(REGTEST))
    wait_until(lambda: txid not in node.mempool(), "the confirmed payment to leave the node's mempool view")

    with pytest.raises(Rejected) as after_block:
        node.broadcast(to_carol)
    assert (after_block.value.rejected_by, after_block.value.code) == ("node", "bad-txns-inputs-missingorspent")

    with pytest.raises(CoreRpcError) as core_after_block:
        core.sendrawtransaction(to_carol)
    assert core_after_block.value.code == RpcErrorCode.VERIFY_ERROR
    assert core_after_block.value.message == "bad-txns-inputs-missingorspent"
