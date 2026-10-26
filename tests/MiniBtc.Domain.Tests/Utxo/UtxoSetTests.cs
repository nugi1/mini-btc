using MiniBtc.Domain.Consensus;
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

        var undo = utxos.Connect(block).Value;

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
        var coinbase = Coinbase(1, 50 * Btc, new TxOut(0, OpReturn), oversized);

        utxos.Connect(MinedBlock(Genesis, coinbase));

        Assert.Equal(new[] { new OutPoint(coinbase.Txid, 0) }, utxos.Coins.Keys);
    }

    [Fact]
    public void Spending_removes_the_coin_records_it_as_undo_and_adds_the_new_outputs()
    {
        var utxos = UtxoSet.AtGenesis(NetworkParams.Regtest);
        var coinbase = Coinbase(1);
        var block1 = MinedBlock(Genesis, coinbase);
        utxos.Connect(block1);
        var spentCoin = utxos.GetCoin(new OutPoint(coinbase.Txid, 0))!;

        var payment = Spend([new OutPoint(coinbase.Txid, 0)], 10 * Btc, 39 * Btc);
        var undo = utxos.Connect(MinedBlock(block1.Hash, Coinbase(2), payment)).Value;

        Assert.Null(utxos.GetCoin(new OutPoint(coinbase.Txid, 0)));
        Assert.Equal(new Coin(payment.Outputs[0], 2, IsCoinbase: false), utxos.GetCoin(new OutPoint(payment.Txid, 0)));
        Assert.Equal(new Coin(payment.Outputs[1], 2, IsCoinbase: false), utxos.GetCoin(new OutPoint(payment.Txid, 1)));
        Assert.Equal(3, utxos.Coins.Count); // block 2's coinbase + two payment outputs

        var txUndo = Assert.Single(undo.Transactions);   // the coinbase gets no undo entry
        Assert.Equal(spentCoin, Assert.Single(txUndo.SpentCoins));
    }

    [Fact]
    public void A_transaction_can_spend_an_output_created_earlier_in_the_same_block()
    {
        var utxos = UtxoSet.AtGenesis(NetworkParams.Regtest);
        var coinbase = Coinbase(1);
        var block1 = MinedBlock(Genesis, coinbase);
        utxos.Connect(block1);

        var parent = Spend([new OutPoint(coinbase.Txid, 0)], 49 * Btc);
        var child = Spend([new OutPoint(parent.Txid, 0)], 48 * Btc);
        var undo = utxos.Connect(MinedBlock(block1.Hash, Coinbase(2), parent, child)).Value;

        Assert.Null(utxos.GetCoin(new OutPoint(parent.Txid, 0)));          // created and spent within block 2
        Assert.NotNull(utxos.GetCoin(new OutPoint(child.Txid, 0)));
        Assert.Equal(new Coin(parent.Outputs[0], 2, false), undo.Transactions[1].SpentCoins[0]);
    }

    [Fact]
    public void Spending_a_missing_coin_fails_and_changes_nothing()
    {
        var utxos = UtxoSet.AtGenesis(NetworkParams.Regtest);
        var block1 = MinedBlock(Genesis, Coinbase(1));
        utxos.Connect(block1);
        var before = utxos.Coins.ToDictionary();
        var eventsBefore = utxos.DomainEvents.Count;

        var spendsNothing = Spend([new OutPoint(Genesis, 7)], 1 * Btc);
        var result = utxos.Connect(MinedBlock(block1.Hash, Coinbase(2), spendsNothing));

        Assert.Equal("bad-txns-inputs-missingorspent", result.Error!.Code);
        Assert.Equal(before.Count, utxos.Coins.Count); // block 2's coinbase was not added either
        Assert.All(before, entry => Assert.Equal(entry.Value, utxos.GetCoin(entry.Key)));
        Assert.Equal(1, utxos.Tip.Height);
        Assert.Equal(eventsBefore, utxos.DomainEvents.Count);
    }

    [Fact]
    public void Two_transactions_in_one_block_cannot_spend_the_same_coin()
    {
        var utxos = UtxoSet.AtGenesis(NetworkParams.Regtest);
        var coinbase = Coinbase(1);
        var block1 = MinedBlock(Genesis, coinbase);
        utxos.Connect(block1);

        var first = Spend([new OutPoint(coinbase.Txid, 0)], 10 * Btc);
        var second = Spend([new OutPoint(coinbase.Txid, 0)], 20 * Btc);
        var result = utxos.Connect(MinedBlock(block1.Hash, Coinbase(2), first, second));

        Assert.Equal("bad-txns-inputs-missingorspent", result.Error!.Code);
        Assert.NotNull(utxos.GetCoin(new OutPoint(coinbase.Txid, 0)));  // still unspent
    }

    [Fact]
    public void Recreating_an_unspent_outpoint_is_rejected_as_bip30()
    {
        var utxos = UtxoSet.AtGenesis(NetworkParams.Regtest);
        var coinbase = Coinbase(1);
        var block1 = MinedBlock(Genesis, coinbase);
        utxos.Connect(block1);

        // The very same coinbase again: same txid, so the same outpoint, which is still unspent.
        var result = utxos.Connect(MinedBlock(block1.Hash, coinbase));

        Assert.Equal("bad-txns-BIP30", result.Error!.Code);
        Assert.Equal(1, utxos.Tip.Height);
    }

    [Fact]
    public void A_block_that_does_not_build_on_the_tip_is_rejected_and_changes_nothing()
    {
        var utxos = UtxoSet.AtGenesis(NetworkParams.Regtest);
        var block1 = MinedBlock(Genesis, Coinbase(1));
        utxos.Connect(block1);

        var sibling = MinedBlock(Genesis, Coinbase(1, 25 * ConsensusConstants.Coin)); // competes with block 1
        var result = utxos.Connect(sibling);

        Assert.Equal("prev-blk-not-found", result.Error!.Code);
        Assert.Equal(new ChainTip(block1.Hash, 1), utxos.Tip);
    }

    [Fact]
    public void Header_rules_run_before_any_coin_moves()
    {
        var utxos = UtxoSet.AtGenesis(NetworkParams.Regtest);
        var coinbase = Coinbase(1);
        var unmined = new MiniBtc.Domain.Blocks.Block(
            MineBadly(Genesis, coinbase.Txid, RegtestBits), [coinbase]);

        var result = utxos.Connect(unmined);

        Assert.Equal("high-hash", result.Error!.Code);
        Assert.Empty(utxos.Coins);
        Assert.Empty(utxos.DomainEvents);
    }
}
