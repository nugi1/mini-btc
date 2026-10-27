"""
python -m wallet [--wallet NAME] new [--wif WIF]
python -m wallet [--wallet NAME] address
python -m wallet [--wallet NAME] balance
python -m wallet [--wallet NAME] send --to ADDRESS --amount BTC [--fee-rate SAT_PER_VB] [--dry-run]

The wallet asks the node which coins it has and Core whether a transaction is acceptable. Until the
node's broadcast gateway exists (E4), `send` hands the signed bytes to Core's sendrawtransaction.
"""

from __future__ import annotations

import argparse
import sys
from decimal import Decimal, InvalidOperation

from btc_rpc import CoreRpc, CoreRpcError
from wallet import store
from wallet.address import AddressError, decode_address
from wallet.builder import DEFAULT_FEE_RATE, InsufficientFunds, Payment, build_payment
from wallet.keys import InvalidKey, Key
from wallet.network import REGTEST
from wallet.node_client import NodeClient, NodeError
from wallet.signing import sign_p2pkh
from wallet.tx import Transaction

SATS_PER_BTC = 100_000_000


def parse_btc(text: str) -> int:
    """'10' or '0.00012345' to satoshis, exactly; more than 8 decimals is an error, not a rounding."""
    try:
        value = Decimal(text)
    except InvalidOperation:
        raise argparse.ArgumentTypeError(f"not an amount: {text}") from None
    sats = value * SATS_PER_BTC
    if not value.is_finite() or sats != sats.to_integral_value() or sats <= 0:
        raise argparse.ArgumentTypeError(f"amount must be positive with at most 8 decimals: {text}")
    return int(sats)


def btc(sats: int) -> str:
    return f"{Decimal(sats) / SATS_PER_BTC:.8f}"


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="python -m wallet", description="A regtest P2PKH wallet for mini-bitcoin.")
    parser.add_argument("--wallet", default="default", metavar="NAME",
                        help="wallet name (stored as ~/.minibtc/NAME.json) or a path to a .json file")
    commands = parser.add_subparsers(dest="command", required=True)

    new = commands.add_parser("new", help="create a wallet with a fresh key")
    new.add_argument("--wif", help="use this regtest private key instead of a random one")
    commands.add_parser("address", help="print the wallet's address")
    commands.add_parser("balance", help="spendable and immature balance, from the node")
    send = commands.add_parser("send", help="pay an address")
    send.add_argument("--to", required=True, metavar="ADDRESS")
    send.add_argument("--amount", required=True, type=parse_btc, metavar="BTC")
    send.add_argument("--fee-rate", type=Decimal, default=DEFAULT_FEE_RATE, metavar="SAT_PER_VB")
    send.add_argument("--dry-run", action="store_true", help="ask Core's testmempoolaccept instead of broadcasting")

    args = parser.parse_args(argv)
    path = store.wallet_path(args.wallet)
    try:
        if args.command == "new":
            key = Key.from_wif(args.wif, REGTEST) if args.wif else Key.generate()
            store.create(path, key)
            print(key.address(REGTEST))
            print(f"saved to {path}", file=sys.stderr)
            return 0

        key = store.load(path)
        address = key.address(REGTEST)
        if args.command == "address":
            print(address)
        elif args.command == "balance":
            balance = NodeClient().balance(address)
            print(f"address    {address}")
            print(f"spendable  {btc(balance['spendable'])} BTC")
            print(f"immature   {btc(balance['immature'])} BTC")
            print(f"total      {btc(balance['total'])} BTC in {balance['utxoCount']} coin(s), node tip {balance['tipHeight']}")
        else:
            return _send(key, args.to, args.amount, args.fee_rate, args.dry_run)
    except (FileExistsError, FileNotFoundError, ValueError, InvalidKey, AddressError,
            NodeError, CoreRpcError, InsufficientFunds) as error:
        print(f"error: {error}", file=sys.stderr)
        return 1
    return 0


def _send(key: Key, to: str, amount: int, fee_rate: Decimal, dry_run: bool) -> int:
    pay_to = decode_address(to, REGTEST).script_pubkey
    tip, coins = NodeClient().utxos(key.address(REGTEST))
    payment = build_payment(coins, pay_to, amount, key.script_pubkey, fee_rate)
    signed = sign_p2pkh(payment.tx, [c.script_pubkey for c in payment.spent], key)
    _describe(payment, signed, tip)

    core = CoreRpc()
    if dry_run:
        verdict = core.testmempoolaccept([signed.hex()])[0]
        if verdict["allowed"]:
            print(f"Core would accept it: txid {verdict['txid']}, vsize {verdict['vsize']}")
            return 0
        print(f"Core would reject it: {verdict['reject-reason']}", file=sys.stderr)
        return 1
    txid = core.sendrawtransaction(signed.hex())
    print(txid)
    return 0


def _describe(payment: Payment, signed: Transaction, tip: int) -> None:
    out = sys.stderr
    print(f"spending {len(payment.spent)} coin(s) (node tip {tip}):", file=out)
    for coin in payment.spent:
        print(f"  {coin.outpoint.txid}:{coin.outpoint.vout}  {btc(coin.value)} BTC  from height {coin.height}", file=out)
    print(f"paying   {btc(payment.tx.outputs[0].value)} BTC", file=out)
    if payment.change:
        print(f"change   {btc(payment.change)} BTC", file=out)
    print(f"fee      {payment.fee} sat for {signed.size} bytes "
          f"({Decimal(payment.fee) / signed.size:.2f} sat/vB)", file=out)
    print(f"raw      {signed.hex()}", file=out)
