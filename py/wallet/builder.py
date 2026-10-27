"""
Building an unsigned payment: which coins to spend, how much change, what fee.

A transaction has no "fee" field. The fee is whatever the inputs hold that the outputs don't claim,
and miners collect it in their coinbase. So the wallet must add a change output back to itself, or
everything above the payment goes to the miner.

The fee is fee_rate * size, and a legacy transaction's size is known before signing, up to the
signature length. Each estimate below is the largest possible size, so the real fee rate is never
below the requested one:

    overhead  10 bytes = version 4 + input count 1 + output count 1 + locktime 4
    input    148 bytes = prevout 36 + script length 1 + scriptSig 107 + sequence 4
                         scriptSig = push(DER signature, at most 71 bytes with low-S, + hash type 1)
                                   + push(compressed public key, 33)
    output    34 bytes = value 8 + script length 1 + P2PKH script 25

148 and 34 are the same numbers Core uses for its dust threshold (src/policy/policy.cpp).
"""

from __future__ import annotations

import math
from dataclasses import dataclass
from decimal import Decimal

from wallet.tx import OutPoint, Transaction, TxIn, TxOut

TX_OVERHEAD_SIZE = 10
P2PKH_INPUT_SIZE = 148
P2PKH_OUTPUT_SIZE = 34

# Core's dust threshold for a P2PKH output at the default dust relay fee of 3 sat/vB:
# (34 + 148) * 3 = 546 satoshis. Smaller outputs cost more to spend than they hold, and Core's
# mempool refuses transactions that create them.
P2PKH_DUST_THRESHOLD = 546

# 1 sat/vB: ten times Core v30's default minimum relay fee rate (0.1 sat/vB).
DEFAULT_FEE_RATE = Decimal(1)


@dataclass(frozen=True)
class Coin:
    """An unspent output as the node reports it."""

    outpoint: OutPoint
    value: int
    script_pubkey: bytes
    height: int
    coinbase: bool
    spendable: bool  # false for a coinbase output that hasn't matured


@dataclass(frozen=True)
class Payment:
    """An unsigned transaction and the coins its inputs spend, in input order."""

    tx: Transaction
    spent: tuple[Coin, ...]
    fee: int
    change: int  # 0 when there is no change output


class InsufficientFunds(Exception):
    pass


def estimated_size(inputs: int, outputs: int) -> int:
    return TX_OVERHEAD_SIZE + P2PKH_INPUT_SIZE * inputs + P2PKH_OUTPUT_SIZE * outputs


def fee_for(inputs: int, outputs: int, fee_rate: Decimal) -> int:
    return math.ceil(Decimal(estimated_size(inputs, outputs)) * fee_rate)


def build_payment(
    coins: list[Coin],
    pay_to: bytes,
    amount: int,
    change_to: bytes,
    fee_rate: Decimal = DEFAULT_FEE_RATE,
) -> Payment:
    """
    Largest-first coin selection: take the biggest spendable coins until they cover the amount and
    the fee. Few inputs keep the transaction small. If what is left over would be dust, it is not
    worth an output, and the miner gets it as extra fee.
    """
    if amount < P2PKH_DUST_THRESHOLD:
        raise ValueError(f"{amount} sat is below the dust threshold of {P2PKH_DUST_THRESHOLD} sat")
    if fee_rate <= 0:
        raise ValueError("the fee rate must be positive")

    candidates = sorted((c for c in coins if c.spendable),
                        key=lambda c: (-c.value, c.outpoint.txid, c.outpoint.vout))
    selected: list[Coin] = []
    total = 0
    for coin in candidates:
        selected.append(coin)
        total += coin.value

        fee = fee_for(len(selected), 2, fee_rate)
        change = total - amount - fee
        if change >= P2PKH_DUST_THRESHOLD:
            return _payment(selected, [TxOut(amount, pay_to), TxOut(change, change_to)], total - amount - change, change)

        if total >= amount + fee_for(len(selected), 1, fee_rate):
            return _payment(selected, [TxOut(amount, pay_to)], total - amount, 0)

    immature = sum(c.value for c in coins if not c.spendable)
    raise InsufficientFunds(
        f"need {amount} sat plus fee, but only {total} sat is spendable"
        + (f" ({immature} sat more is immature coinbase)" if immature else ""))


def _payment(selected: list[Coin], outputs: list[TxOut], fee: int, change: int) -> Payment:
    tx = Transaction(tuple(TxIn(c.outpoint) for c in selected), tuple(outputs))
    return Payment(tx, tuple(selected), fee, change)
