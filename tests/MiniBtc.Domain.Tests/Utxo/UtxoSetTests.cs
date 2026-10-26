using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Scripts;
using MiniBtc.Domain.Tests.TestData;
using MiniBtc.Domain.Transactions;
using MiniBtc.Domain.Utxo;
using static MiniBtc.Domain.Tests.TestData.TestChain;

namespace MiniBtc.Domain.Tests.Utxo;

public class UtxoSetTests
{
    private const long Btc = ConsensusConstants.Coin;

    [Fact]
    public void Starts_at_genesis_with_no_coins_because_the_genesis_coinbase_is_unspendable()
    {
        var utxos = UtxoSet.AtGenesis(NetworkParams.Regtest);

        Assert.Equal("regtest", utxos.Id);
        Assert.Equal(new ChainTip(Genesis, 0), utxos.Tip);
        Assert.Empty(utxos.Coins);
        Assert.Null(utxos.GetCoin(new OutPoint(NetworkParams.Regtest.GenesisBlock.Transactions[0].Txid, 0)));
    }

    [Fact]
    public void Connecting_a_block_adds_its_outputs_as_coins_and_moves_the_tip()
    {
        var utxos = UtxoSet.AtGenesis(NetworkParams.Regtest);
        var coinbase = Coinbase(1);
        var block = MinedBlock(Genesis, coinbase);

        var undo = utxos.Connect(block).Value.Undo;

        Assert.Equal(new ChainTip(block.Hash, 1), utxos.Tip);
        var coin = Assert.Single(utxos.Coins);
        Assert.Equal(new OutPoint(coinbase.Txid, 0), coin.Key);
        Assert.Equal(new Coin(coinbase.Outputs[0], Height: 1, IsCoinbase: true), coin.Value);
        Assert.Empty(undo.Transactions); // a coinbase spends nothing
    }

    [Fact]
    public void Connecting_raises_block_connected()
    {
        var utxos = UtxoSet.AtGenesis(NetworkParams.Regtest);
        var block = MinedBlock(Genesis, Coinbase(1));

        utxos.Connect(block);

        Assert.Equal(new BlockConnected(block.Hash, 1, 1), Assert.Single(utxos.DomainEvents));
    }

    [Fact]
    public void Op_return_and_oversized_outputs_never_become_coins()
    {
        var utxos = UtxoSet.AtGenesis(NetworkParams.Regtest);
        var oversized = new TxOut(1, new byte[TxOut.MaxScriptSize + 1]);
        var coinbase = Coinbase(1, 50 * Btc - 1, new TxOut(0, OpReturn), oversized); // total exactly 50 BTC

        utxos.Connect(MinedBlock(Genesis, coinbase));

        Assert.Equal(new[] { new OutPoint(coinbase.Txid, 0) }, utxos.Coins.Keys);
    }

    [Fact]
    public void Spending_removes_the_coin_records_it_as_undo_and_adds_the_new_outputs()
    {
        var (utxos, coinbase) = ChainWithCoinbaseAtHeight1();
        var spentCoin = utxos.GetCoin(new OutPoint(coinbase.Txid, 0))!;
        var coinsBefore = utxos.Coins.Count;

        var payment = Spend([new OutPoint(coinbase.Txid, 0)], 10 * Btc, 39 * Btc);
        var undo = utxos.Connect(MinedBlock(utxos.Tip.Hash, Coinbase(101), payment)).Value.Undo;

        Assert.Null(utxos.GetCoin(new OutPoint(coinbase.Txid, 0)));
        Assert.Equal(new Coin(payment.Outputs[0], 101, IsCoinbase: false), utxos.GetCoin(new OutPoint(payment.Txid, 0)));
        Assert.Equal(new Coin(payment.Outputs[1], 101, IsCoinbase: false), utxos.GetCoin(new OutPoint(payment.Txid, 1)));
        Assert.Equal(coinsBefore - 1 + 3, utxos.Coins.Count); // one spent; new coinbase + two payment outputs

        var txUndo = Assert.Single(undo.Transactions);   // the coinbase gets no undo entry
        Assert.Equal(spentCoin, Assert.Single(txUndo.SpentCoins));
    }

    [Fact]
    public void A_transaction_can_spend_an_output_created_earlier_in_the_same_block()
    {
        var (utxos, coinbase) = ChainWithCoinbaseAtHeight1();

        var parent = Spend([new OutPoint(coinbase.Txid, 0)], 49 * Btc);
        var child = Spend([new OutPoint(parent.Txid, 0)], 48 * Btc);
        var undo = utxos.Connect(MinedBlock(utxos.Tip.Hash, Coinbase(101), parent, child)).Value.Undo;

        Assert.Null(utxos.GetCoin(new OutPoint(parent.Txid, 0)));          // created and spent within block 101
        Assert.NotNull(utxos.GetCoin(new OutPoint(child.Txid, 0)));
        Assert.Equal(new Coin(parent.Outputs[0], 101, false), undo.Transactions[1].SpentCoins[0]);
    }

    [Fact]
    public void Spending_a_missing_coin_fails_and_changes_nothing()
    {
        var (utxos, _) = ChainWithCoinbaseAtHeight1(tipHeight: 1);
        var coinsBefore = utxos.Coins.ToDictionary();
        var eventsBefore = utxos.DomainEvents.Count;

        var spendsNothing = Spend([new OutPoint(Genesis, 7)], 1 * Btc);
        var result = utxos.Connect(MinedBlock(utxos.Tip.Hash, Coinbase(2), spendsNothing));

        Assert.Equal("bad-txns-inputs-missingorspent", result.Error!.Code);
        Assert.Equal(coinsBefore.Count, utxos.Coins.Count); // block 2's coinbase was not added either
        Assert.All(coinsBefore, entry => Assert.Equal(entry.Value, utxos.GetCoin(entry.Key)));
        Assert.Equal(1, utxos.Tip.Height);
        Assert.Equal(eventsBefore, utxos.DomainEvents.Count);
    }

    [Fact]
    public void Two_transactions_in_one_block_cannot_spend_the_same_coin()
    {
        var (utxos, coinbase) = ChainWithCoinbaseAtHeight1();

        var first = Spend([new OutPoint(coinbase.Txid, 0)], 10 * Btc);
        var second = Spend([new OutPoint(coinbase.Txid, 0)], 20 * Btc);
        var result = utxos.Connect(MinedBlock(utxos.Tip.Hash, Coinbase(101), first, second));

        Assert.Equal("bad-txns-inputs-missingorspent", result.Error!.Code);
        Assert.NotNull(utxos.GetCoin(new OutPoint(coinbase.Txid, 0)));  // still unspent
    }

    [Fact]
    public void A_coinbase_is_spendable_exactly_100_blocks_later()
    {
        var (utxos, coinbase) = ChainWithCoinbaseAtHeight1(tipHeight: 99);
        var payment = Spend([new OutPoint(coinbase.Txid, 0)], 49 * Btc);

        var atDepth99 = utxos.Connect(MinedBlock(utxos.Tip.Hash, Coinbase(100), payment));

        Assert.Equal("bad-txns-premature-spend-of-coinbase", atDepth99.Error!.Code);
        Assert.Equal("tried to spend coinbase at depth 99", atDepth99.Error.Message);

        Connect(utxos, MinedBlock(utxos.Tip.Hash, Coinbase(100)));
        var atDepth100 = utxos.Connect(MinedBlock(utxos.Tip.Hash, Coinbase(101), payment));

        Assert.True(atDepth100.IsSuccess, atDepth100.Error?.ToString());
    }

    [Fact]
    public void Spending_more_than_the_inputs_is_rejected()
    {
        var (utxos, coinbase) = ChainWithCoinbaseAtHeight1();
        var inflation = Spend([new OutPoint(coinbase.Txid, 0)], 51 * Btc);

        var result = utxos.Connect(MinedBlock(utxos.Tip.Hash, Coinbase(101), inflation));

        Assert.Equal("bad-txns-in-belowout", result.Error!.Code);
    }

    [Fact]
    public void Context_free_transaction_rules_run_before_any_coin_is_looked_up()
    {
        var (utxos, coinbase) = ChainWithCoinbaseAtHeight1();
        // Spends a missing coin AND repeats an input: Core's CheckTransaction reports the duplicate first.
        var bad = Spend([new OutPoint(coinbase.Txid, 5), new OutPoint(coinbase.Txid, 5)], 1 * Btc);

        var result = utxos.Connect(MinedBlock(utxos.Tip.Hash, Coinbase(101), bad));

        Assert.Equal("bad-txns-inputs-duplicate", result.Error!.Code);
    }

    [Fact]
    public void Recreating_an_unspent_outpoint_is_rejected_as_bip30()
    {
        var (utxos, coinbase) = ChainWithCoinbaseAtHeight1(tipHeight: 1);

        // The very same coinbase again: same txid, so the same outpoint, which is still unspent.
        var result = utxos.Connect(MinedBlock(utxos.Tip.Hash, coinbase));

        Assert.Equal("bad-txns-BIP30", result.Error!.Code);
        Assert.Equal(1, utxos.Tip.Height);
    }

    [Fact]
    public void A_block_that_does_not_build_on_the_tip_is_rejected_and_changes_nothing()
    {
        var (utxos, _) = ChainWithCoinbaseAtHeight1(tipHeight: 1);
        var tip = utxos.Tip;

        var sibling = MinedBlock(Genesis, Coinbase(1, 25 * Btc)); // competes with block 1
        var result = utxos.Connect(sibling);

        Assert.Equal("prev-blk-not-found", result.Error!.Code);
        Assert.Equal(tip, utxos.Tip);
    }

    [Fact]
    public void A_correctly_signed_p2pkh_spend_is_verified_by_our_own_code()
    {
        var key = TestSigner.KeyOne;
        var (utxos, coinbase) = ChainWithCoinbaseAtHeight1(payTo: key.P2pkhScript);
        var payment = key.SignInput(Spend([new OutPoint(coinbase.Txid, 0)], 49 * Btc), 0);

        var result = utxos.Connect(MinedBlock(utxos.Tip.Hash, Coinbase(101), payment));

        Assert.True(result.IsSuccess, result.Error?.ToString());
        Assert.Empty(result.Value.UnverifiedScripts);   // checked, not taken on trust
        Assert.Equal(1 * Btc, result.Value.Fees);
    }

    [Fact]
    public void A_bad_signature_makes_the_block_invalid_with_cores_reason()
    {
        var key = TestSigner.KeyOne;
        var (utxos, coinbase) = ChainWithCoinbaseAtHeight1(payTo: key.P2pkhScript);
        var signed = key.SignInput(Spend([new OutPoint(coinbase.Txid, 0)], 49 * Btc), 0);
        // Same signature, but the output now pays 1 sat more than what was signed.
        var tampered = new Transaction(signed.Version, signed.Inputs,
            [new TxOut(49 * Btc - 1, signed.Outputs[0].ScriptPubKey.Span)], signed.LockTime);

        var result = utxos.Connect(MinedBlock(utxos.Tip.Hash, Coinbase(101), tampered));

        Assert.Equal($"block-script-verify-flag-failed ({ScriptErrors.EvalFalse})", result.Error!.Code);
        Assert.NotNull(utxos.GetCoin(new OutPoint(coinbase.Txid, 0)));
    }

    [Fact]
    public void Scripts_we_do_not_verify_are_accepted_and_flagged()
    {
        var (utxos, coinbase) = ChainWithCoinbaseAtHeight1(); // pays to OP_TRUE, not P2PKH
        var payment = Spend([new OutPoint(coinbase.Txid, 0)], 49 * Btc);

        var result = utxos.Connect(MinedBlock(utxos.Tip.Hash, Coinbase(101), payment));

        var flagged = Assert.Single(result.Value.UnverifiedScripts);
        Assert.Equal((payment.Txid, 0), (flagged.Txid, flagged.InputIndex));
    }

    [Fact]
    public void Connect_reports_the_net_change_to_the_set()
    {
        var (utxos, coinbase) = ChainWithCoinbaseAtHeight1();
        var parent = Spend([new OutPoint(coinbase.Txid, 0)], 49 * Btc);
        var child = Spend([new OutPoint(parent.Txid, 0)], 48 * Btc);
        var newCoinbase = Coinbase(101);

        var changes = utxos.Connect(MinedBlock(utxos.Tip.Hash, newCoinbase, parent, child)).Value.Changes;

        Assert.Equal(new[] { new OutPoint(coinbase.Txid, 0) }, changes.Spent);
        // The parent's output was created and spent inside the block: the set never held it.
        var created = changes.Created.Select(entry => entry.Key).ToList();
        Assert.Equal(2, created.Count);
        Assert.True(new HashSet<OutPoint> { new(newCoinbase.Txid, 0), new(child.Txid, 0) }.SetEquals(created));
    }

    [Fact]
    public void A_restored_set_continues_exactly_where_the_original_was()
    {
        var (original, coinbase) = ChainWithCoinbaseAtHeight1();

        var restored = UtxoSet.Restore(NetworkParams.Regtest, original.Tip, original.Coins);
        var payment = Spend([new OutPoint(coinbase.Txid, 0)], 49 * Btc);
        var result = restored.Connect(MinedBlock(restored.Tip.Hash, Coinbase(101), payment));

        Assert.True(result.IsSuccess, result.Error?.ToString());
        Assert.Equal(original.Coins.Count - 1 + 2, restored.Coins.Count);
        Assert.Equal(100, original.Tip.Height); // the original is untouched: Restore copied the coins
    }

    [Fact]
    public void Header_rules_run_before_any_coin_moves()
    {
        var utxos = UtxoSet.AtGenesis(NetworkParams.Regtest);
        var coinbase = Coinbase(1);
        var unmined = new MiniBtc.Domain.Blocks.Block(MineBadly(Genesis, coinbase.Txid, RegtestBits), [coinbase]);

        var result = utxos.Connect(unmined);

        Assert.Equal("high-hash", result.Error!.Code);
        Assert.Empty(utxos.Coins);
        Assert.Empty(utxos.DomainEvents);
    }
}
