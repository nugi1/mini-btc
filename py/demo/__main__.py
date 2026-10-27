"""
python -m demo — the activity a visitor to the explorer should see.

Alice and Bob (fixed keys, so restarts reuse their coins) pay each other small random amounts
through the node's broadcast gateway; a miner key mines a block every MINIBTC_DEMO_BLOCK_SECONDS.
Now and then the driver also tries to spend a coin twice, so the explorer shows the node refusing it.

It also runs the faucet for the explorer's browser wallet (demo/faucet.py): 5 BTC from the miner's
coins to any regtest address, confirmed at once by mining a block.
Regtest coins only: nothing here has any value.
"""

from __future__ import annotations

import os
import random
import threading
import time

from btc_rpc import CoreRpc, CoreRpcError
from demo.faucet import FaucetError, serve
from wallet.address import AddressError, decode_address
from wallet.builder import Coin, InsufficientFunds, build_payment
from wallet.keys import Key
from wallet.network import REGTEST
from wallet.node_client import NodeClient, NodeError, Rejected
from wallet.signing import sign_p2pkh

BTC = 100_000_000
ALICE, BOB, MINER = Key.from_int(1001), Key.from_int(1002), Key.from_int(1003)
BLOCK_SECONDS = int(os.environ.get("MINIBTC_DEMO_BLOCK_SECONDS", "60"))
TX_SECONDS = int(os.environ.get("MINIBTC_DEMO_TX_SECONDS", "20"))
FAUCET_PORT = int(os.environ.get("MINIBTC_FAUCET_PORT", "8090"))
FAUCET_AMOUNT = 5 * BTC

core = CoreRpc()
node = NodeClient()
pending: set[tuple[str, int]] = set()  # outpoints spent by our own unconfirmed payments
pending_tip = -1
# The demo loop and the faucet's request threads share the keys, `pending` and the chain: one at a time.
chain_lock = threading.RLock()


def log(message: str) -> None:
    print(time.strftime("%H:%M:%S"), message, flush=True)


def wait_for_services() -> None:
    while True:
        try:
            core.getblockchaininfo()
            node.balance(ALICE.address(REGTEST))
            return
        except (CoreRpcError, NodeError) as error:
            log(f"waiting for Core and the node: {error}")
            time.sleep(5)


def coins(key: Key) -> tuple[int, list[Coin]]:
    global pending, pending_tip
    tip, owned = node.utxos(key.address(REGTEST))
    if tip != pending_tip:  # a new block: what was pending is confirmed or gone
        pending, pending_tip = set(), tip
    return tip, [c for c in owned if c.spendable and (c.outpoint.txid, c.outpoint.vout) not in pending]


def mine(count: int, key: Key) -> list[str]:
    """Mines with Core, then waits until our node has validated the blocks, so its API shows them."""
    hashes = core.generatetoaddress(count, key.address(REGTEST))
    target = core.getblockcount()
    deadline = time.monotonic() + 30
    while node.balance(key.address(REGTEST))["tipHeight"] < target and time.monotonic() < deadline:
        time.sleep(0.25)
    return hashes


def ensure_funds() -> None:
    spendable = sum(c.value for key in (ALICE, BOB) for c in coins(key)[1])
    if spendable < 1 * BTC:
        log("funding Alice: mining 101 blocks so her first coinbase matures")
        mine(101, ALICE)
    if sum(c.value for c in coins(MINER)[1]) < 2 * FAUCET_AMOUNT:
        log("funding the faucet: mining 101 blocks so the miner's first coinbase matures")
        mine(101, MINER)


def pay() -> None:
    payer, payee = random.choice([(ALICE, BOB), (BOB, ALICE)])
    _, available = coins(payer)
    if not available:
        payer, payee = payee, payer
        _, available = coins(payer)
    amount = random.randint(10, 200) * BTC // 100  # 0.10 to 2.00 BTC
    try:
        payment = build_payment(available, payee.script_pubkey, amount, payer.script_pubkey)
    except InsufficientFunds:
        log("not enough confirmed coins this round")
        return
    signed = sign_p2pkh(payment.tx, [c.script_pubkey for c in payment.spent], payer)
    try:
        txid = node.broadcast(signed.hex())
        pending.update((c.outpoint.txid, c.outpoint.vout) for c in payment.spent)
        log(f"paid {amount / BTC:.2f} BTC: {txid}")
    except Rejected as rejection:
        log(f"rejected by {rejection.rejected_by}: {rejection.code}")

    if random.random() < 0.15:
        double_spend(payer, payment.spent[0])


def double_spend(payer: Key, coin: Coin) -> None:
    """Spend a coin the node already saw spent: the node must refuse it before Core sees it."""
    again = build_payment([coin], MINER.script_pubkey, coin.value // 2, payer.script_pubkey)
    try:
        node.broadcast(sign_p2pkh(again.tx, [coin.script_pubkey], payer).hex())
        log("double spend was accepted?!")
    except Rejected as rejection:
        log(f"double spend refused by {rejection.rejected_by}: {rejection.code}")


def faucet_fund(address: str) -> dict:
    try:
        destination = decode_address(address, REGTEST)
    except AddressError as error:
        raise FaucetError(400, str(error)) from None
    with chain_lock:
        ensure_funds()
        payment = build_payment(coins(MINER)[1], destination.script_pubkey, FAUCET_AMOUNT, MINER.script_pubkey)
        signed = sign_p2pkh(payment.tx, [c.script_pubkey for c in payment.spent], MINER)
        try:
            txid = node.broadcast(signed.hex())
        except Rejected as rejection:
            raise FaucetError(502, f"rejected by {rejection.rejected_by}: {rejection.code}") from None
        pending.update((c.outpoint.txid, c.outpoint.vout) for c in payment.spent)
        block = mine(1, MINER)[0]
        height = core.getblockcount()
    log(f"faucet paid {FAUCET_AMOUNT / BTC:.0f} BTC to {address}: {txid}, block {height}")
    return {"txid": txid, "amount": FAUCET_AMOUNT, "height": height, "block": block}


def faucet_mine() -> dict:
    with chain_lock:
        block = mine(1, MINER)[0]
        height = core.getblockcount()
    log(f"faucet mined block {height}")
    return {"height": height, "block": block}


def main() -> None:
    wait_for_services()
    with chain_lock:
        ensure_funds()
    serve(FAUCET_PORT, faucet_fund, faucet_mine, log)
    log(f"faucet listening on :{FAUCET_PORT}")
    log(f"running: a payment every ~{TX_SECONDS} s, a block every ~{BLOCK_SECONDS} s")
    next_block = time.monotonic() + BLOCK_SECONDS
    while True:
        try:
            with chain_lock:
                pay()
                if time.monotonic() >= next_block:
                    block = core.generatetoaddress(1, MINER.address(REGTEST))[0]
                    log(f"mined {block}")
                    next_block = time.monotonic() + BLOCK_SECONDS
                    ensure_funds()
        except (CoreRpcError, NodeError) as error:
            log(f"error, retrying: {error}")
        time.sleep(TX_SECONDS * random.uniform(0.6, 1.4))


if __name__ == "__main__":
    main()
