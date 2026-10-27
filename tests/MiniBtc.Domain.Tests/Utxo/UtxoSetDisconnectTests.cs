using MiniBtc.Domain.Blocks;
using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Transactions;
using MiniBtc.Domain.Utxo;
using static MiniBtc.Domain.Tests.TestData.TestChain;

namespace MiniBtc.Domain.Tests.Utxo;

public class UtxoSetDisconnectTests
{
    private const long Btc = ConsensusConstants.Coin;

    private readonly UtxoSet _utxos;
    private readonly OutPoint _coinbaseCoin;

    public UtxoSetDisconnectTests()
    {
        var (utxos, coinbase) = ChainWithCoinbaseAtHeight1(tipHeight: 100);
        _utxos = utxos;
        _coinbaseCoin = new OutPoint(coinbase.Txid, 0);
    }

    private (Block Block, BlockUndo Undo) ConnectNext(params Transaction[] transactions)
    {
        var block = MinedBlock(_utxos.Tip.Hash, [Coinbase(_utxos.Tip.Height + 1), .. transactions]); // leaves fees unclaimed, which is allowed
        var result = _utxos.Connect(block);
        Assert.True(result.IsSuccess, result.Error?.ToString());
        return (block, result.Value.Undo);
    }

    [Fact]
    public void Disconnecting_the_tip_restores_the_set_and_the_tip_before_it()
    {
        var coinsBefore = _utxos.Coins.ToDictionary();
        var tipBefore = _utxos.Tip;
        var payment = Spend([_coinbaseCoin], 10 * Btc, 39 * Btc);
        var (block, undo) = ConnectNext(payment);

        var disconnected = _utxos.Disconnect(block, undo);

        Assert.True(disconnected.IsSuccess, disconnected.Error?.ToString());
        Assert.Equal(tipBefore, _utxos.Tip);
        Assert.Equal(coinsBefore.OrderBy(c => c.Key.ToString()), _utxos.Coins.OrderBy(c => c.Key.ToString()));
        Assert.Equal(new[] { payment.Txid }, disconnected.Value.Unconfirmed.Select(tx => tx.Txid));
        Assert.Equal(new BlockDisconnected(block.Hash, 101, 2), _utxos.DomainEvents[^1]);
    }

    [Fact]
    public void The_changes_name_the_outputs_removed_and_the_coins_restored()
    {
        var original = _utxos.GetCoin(_coinbaseCoin)!;
        var payment = Spend([_coinbaseCoin], 10 * Btc, 39 * Btc);
        var (block, undo) = ConnectNext(payment);

        var changes = _utxos.Disconnect(block, undo).Value.Changes;

        Assert.Equal(
            new[] { new OutPoint(block.Transactions[0].Txid, 0), new OutPoint(payment.Txid, 0), new OutPoint(payment.Txid, 1) }.OrderBy(o => o.ToString()),
            changes.Spent.OrderBy(o => o.ToString()));
        Assert.Equal(new KeyValuePair<OutPoint, Coin>(_coinbaseCoin, original), Assert.Single(changes.Created));
    }

    [Fact]
    public void A_coin_created_and_spent_inside_the_block_leaves_no_trace()
    {
        var coinsBefore = _utxos.Coins.ToDictionary();
        var parent = Spend([_coinbaseCoin], 49 * Btc);
        var child = Spend([new OutPoint(parent.Txid, 0)], 48 * Btc);
        var (block, undo) = ConnectNext(parent, child);

        var changes = _utxos.Disconnect(block, undo).Value.Changes;

        Assert.DoesNotContain(new OutPoint(parent.Txid, 0), changes.Spent);
        Assert.DoesNotContain(changes.Created, c => c.Key == new OutPoint(parent.Txid, 0));
        Assert.Equal(coinsBefore.OrderBy(c => c.Key.ToString()), _utxos.Coins.OrderBy(c => c.Key.ToString()));
    }

    [Fact]
    public void The_same_block_connects_again_after_a_disconnect()
    {
        var (block, undo) = ConnectNext(Spend([_coinbaseCoin], 49 * Btc));
        _utxos.Disconnect(block, undo);

        Assert.True(_utxos.Connect(block).IsSuccess);
        Assert.Equal(block.Hash, _utxos.Tip.Hash);
    }

    [Fact]
    public void Only_the_tip_can_be_disconnected()
    {
        var (first, firstUndo) = ConnectNext();
        ConnectNext();

        var result = _utxos.Disconnect(first, firstUndo);

        Assert.Equal(DisconnectErrors.NotTip, result.Error!.Code);
        Assert.Equal(102, _utxos.Tip.Height);
    }

    [Fact]
    public void Undo_data_that_does_not_match_the_block_changes_nothing()
    {
        var coinsBefore = _utxos.Coins.Count;
        var (block, _) = ConnectNext(Spend([_coinbaseCoin], 49 * Btc));

        var result = _utxos.Disconnect(block, new BlockUndo([]));

        Assert.Equal(DisconnectErrors.BadUndo, result.Error!.Code);
        Assert.Equal(block.Hash, _utxos.Tip.Hash);
        Assert.Equal(coinsBefore - 1 + 2, _utxos.Coins.Count); // the coinbase spent; the new coinbase and the payment added
    }
}
