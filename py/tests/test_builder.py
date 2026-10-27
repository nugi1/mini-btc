"""Coin selection, change and fees."""

from __future__ import annotations

from decimal import Decimal

import pytest

from wallet.builder import (
    P2PKH_DUST_THRESHOLD, Coin, InsufficientFunds, build_payment, estimated_size, fee_for,
)
from wallet.keys import Key
from wallet.tx import OutPoint

BTC = 100_000_000
ALICE = Key.from_int(1).script_pubkey
BOB = Key.from_int(2).script_pubkey


def coin(value: int, n: int = 0, spendable: bool = True, height: int = 1) -> Coin:
    return Coin(OutPoint(f"{n:064x}", 0), value, ALICE, height, coinbase=True, spendable=spendable)


def test_sizes_match_the_documented_formula():
    assert estimated_size(1, 2) == 10 + 148 + 2 * 34 == 226
    assert fee_for(1, 2, Decimal(1)) == 226
    assert fee_for(1, 2, Decimal("0.5")) == 113
    assert fee_for(1, 1, Decimal("0.5")) == 96  # 192 * 0.5 = 96 exactly
    assert fee_for(3, 1, Decimal("0.3")) == 147  # 488 * 0.3 = 146.4, rounded up


def test_pays_10_btc_from_one_coinbase_with_change_back():
    payment = build_payment([coin(50 * BTC)], BOB, 10 * BTC, ALICE)

    assert [(o.value, o.script_pubkey) for o in payment.tx.outputs] == [
        (10 * BTC, BOB), (40 * BTC - 226, ALICE)]
    assert payment.fee == 226
    assert payment.change == 40 * BTC - 226
    assert sum(c.value for c in payment.spent) == sum(o.value for o in payment.tx.outputs) + payment.fee


def test_takes_the_largest_coins_first_and_no_more_than_needed():
    coins = [coin(1 * BTC, 1), coin(30 * BTC, 2), coin(20 * BTC, 3), coin(5 * BTC, 4)]

    payment = build_payment(coins, BOB, 45 * BTC, ALICE)

    assert [c.value for c in payment.spent] == [30 * BTC, 20 * BTC]
    assert [i.prevout for i in payment.tx.inputs] == [c.outpoint for c in payment.spent]


def test_immature_coins_are_never_selected():
    coins = [coin(50 * BTC, 1, spendable=False), coin(50 * BTC, 2)]

    payment = build_payment(coins, BOB, 10 * BTC, ALICE)

    assert payment.spent == (coins[1],)


def test_leftover_below_the_dust_threshold_goes_to_the_fee_instead_of_change():
    one_output_fee = fee_for(1, 1, Decimal(1))  # 192
    leftover = 500  # less than 546 + the 34 bytes a change output would add
    payment = build_payment([coin(10 * BTC + one_output_fee + leftover)], BOB, 10 * BTC, ALICE)

    assert len(payment.tx.outputs) == 1
    assert payment.change == 0
    assert payment.fee == one_output_fee + leftover


def test_change_of_exactly_the_dust_threshold_is_kept():
    payment = build_payment([coin(10 * BTC + 226 + P2PKH_DUST_THRESHOLD)], BOB, 10 * BTC, ALICE)

    assert payment.change == P2PKH_DUST_THRESHOLD
    assert payment.fee == 226


def test_not_enough_spendable_coins_says_how_much_is_immature():
    with pytest.raises(InsufficientFunds, match=r"only 100000000 sat is spendable \(5000000000 sat more is immature"):
        build_payment([coin(1 * BTC, 1), coin(50 * BTC, 2, spendable=False)], BOB, 2 * BTC, ALICE)


def test_the_exact_amount_plus_fee_is_enough():
    payment = build_payment([coin(10 * BTC + 192)], BOB, 10 * BTC, ALICE)

    assert (payment.fee, payment.change) == (192, 0)


def test_dust_payments_are_refused():
    with pytest.raises(ValueError, match="dust"):
        build_payment([coin(BTC)], BOB, 545, ALICE)


def test_the_unsigned_transaction_is_version_2_final_with_empty_scriptsigs():
    tx = build_payment([coin(50 * BTC)], BOB, BTC, ALICE).tx

    assert (tx.version, tx.locktime) == (2, 0)
    assert all(i.script_sig == b"" and i.sequence == 0xFFFFFFFF for i in tx.inputs)
