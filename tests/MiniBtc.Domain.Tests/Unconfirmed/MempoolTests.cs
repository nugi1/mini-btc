using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Scripts;
using MiniBtc.Domain.Tests.TestData;
using MiniBtc.Domain.Transactions;
using MiniBtc.Domain.Unconfirmed;
using MiniBtc.Domain.Utxo;
using static MiniBtc.Domain.Tests.TestData.TestChain;

namespace MiniBtc.Domain.Tests.Unconfirmed;

public class MempoolTests
{
    private const long Btc = ConsensusConstants.Coin;
    private static readonly byte[] KeyOne = TestSigner.KeyOne.P2pkhScript;

    private readonly UtxoSet _utxos;
    private readonly OutPoint _coin;
    private readonly Mempool _mempool = new(NetworkParams.Regtest);

    public MempoolTests()
    {
        // Block 1 pays 50 BTC to key one; at tip 100 the next block (101) may spend it.
        var (utxos, coinbase) = ChainWithCoinbaseAtHeight1(tipHeight: 100, payTo: KeyOne);
        _utxos = utxos;
        _coin = new OutPoint(coinbase.Txid, 0);
    }

    private int SpendHeight => _utxos.Tip.Height + 1;

    private CheckedTransaction Accepted(Transaction tx)
    {
        var result = _mempool.Check(tx, _utxos.GetCoin, SpendHeight);
        Assert.True(result.IsSuccess, result.Error?.ToString());
        return result.Value;
    }

    private string Rejected(Transaction tx, Func<OutPoint, Coin?>? coins = null, int? spendHeight = null)
    {
        var result = _mempool.Check(tx, coins ?? _utxos.GetCoin, spendHeight ?? SpendHeight);
        Assert.True(result.IsFailure, $"{tx.Txid} was accepted");
        return result.Error!.Code;
    }

    /// <summary>Spends <paramref name="from"/> (a key-one coin) into one output to key one, signed.</summary>
    private static Transaction Pay(OutPoint from, long value) =>
        TestSigner.KeyOne.SignInput(new Transaction(2, [new TxIn(from, ReadOnlySpan<byte>.Empty)], [new TxOut(value, KeyOne)], 0), 0);

    [Fact]
    public void A_signed_spend_of_a_mature_coin_passes_with_its_fee_and_changes_nothing()
    {
        var checkedTx = Accepted(Pay(_coin, 49 * Btc));

        Assert.Equal(1 * Btc, checkedTx.Fee);
        Assert.Empty(checkedTx.UnverifiedInputs);
        Assert.Equal(0, _mempool.Count);
    }

    [Fact]
    public void A_second_spend_of_the_same_coin_is_refused_as_a_replacement()
    {
        var first = Pay(_coin, 49 * Btc);
        _mempool.Add(first);

        Assert.Equal(MempoolErrors.ReplacementDisallowed, Rejected(Pay(_coin, 48 * Btc)));
        Assert.Equal(first.Txid, _mempool.SpenderOf(_coin));
    }

    [Fact]
    public void The_same_transaction_twice_is_already_in_the_mempool()
    {
        var tx = Pay(_coin, 49 * Btc);
        _mempool.Add(tx);

        Assert.Equal(MempoolErrors.AlreadyInMempool, Rejected(tx));
    }

    [Fact]
    public void A_coin_that_does_not_exist_is_missing()
    {
        Assert.Equal(MempoolErrors.MissingInputs, Rejected(Pay(new OutPoint(_coin.Txid, 1), 1 * Btc)));
    }

    [Fact]
    public void A_transaction_whose_outputs_are_already_coins_is_already_known()
    {
        var tx = Pay(_coin, 49 * Btc);
        Coin? AfterItConfirmed(OutPoint outPoint) =>
            outPoint == new OutPoint(tx.Txid, 0) ? new Coin(tx.Outputs[0], 101, IsCoinbase: false) : null;

        Assert.Equal(MempoolErrors.AlreadyKnown, Rejected(tx, AfterItConfirmed));
    }

    [Fact]
    public void A_coinbase_99_blocks_deep_is_premature()
    {
        Assert.Equal("bad-txns-premature-spend-of-coinbase", Rejected(Pay(_coin, 49 * Btc), spendHeight: 100));
    }

    [Fact]
    public void A_signature_over_different_outputs_fails_the_script_check()
    {
        var signed = Pay(_coin, 49 * Btc);
        var tampered = new Transaction(signed.Version, signed.Inputs, [new TxOut(49 * Btc + 1, KeyOne)], signed.LockTime);

        Assert.Equal($"mempool-script-verify-flag-failed ({ScriptErrors.EvalFalse})", Rejected(tampered));
    }

    [Fact]
    public void Outputs_above_inputs_fail_the_value_balance()
    {
        Assert.Equal("bad-txns-in-belowout", Rejected(Pay(_coin, 51 * Btc)));
    }

    [Fact]
    public void A_coinbase_is_never_accepted()
    {
        Assert.Equal(MempoolErrors.Coinbase, Rejected(Coinbase(101)));
    }

    [Fact]
    public void A_child_may_spend_its_unconfirmed_parent()
    {
        var parent = Pay(_coin, 49 * Btc);
        _mempool.Add(parent);

        var child = Accepted(Pay(new OutPoint(parent.Txid, 0), 48 * Btc));

        Assert.Equal(1 * Btc, child.Fee);
    }

    [Fact]
    public void Removing_a_transaction_frees_the_coin_it_spent()
    {
        var first = Pay(_coin, 49 * Btc);
        _mempool.Add(first);

        Assert.True(_mempool.Remove(first.Txid));

        Assert.Null(_mempool.SpenderOf(_coin));
        Accepted(Pay(_coin, 48 * Btc));
        Assert.False(_mempool.Remove(first.Txid));
    }

    [Fact]
    public void A_block_confirming_a_transaction_removes_it_and_keeps_its_children()
    {
        var parent = Pay(_coin, 49 * Btc);
        var child = Pay(new OutPoint(parent.Txid, 0), 48 * Btc);
        _mempool.Add(parent);
        _mempool.Add(child);

        var removed = _mempool.RemoveForBlock(MinedBlock(_utxos.Tip.Hash, Coinbase(101), parent));

        Assert.Equal(new[] { parent.Txid }, removed);
        Assert.True(_mempool.Contains(child.Txid));
    }

    [Fact]
    public void A_block_spending_the_same_coin_evicts_the_conflict_and_its_descendants()
    {
        var parent = Pay(_coin, 49 * Btc);
        var child = Pay(new OutPoint(parent.Txid, 0), 48 * Btc);
        _mempool.Add(parent);
        _mempool.Add(child);
        var winner = Pay(_coin, 47 * Btc);

        var removed = _mempool.RemoveForBlock(MinedBlock(_utxos.Tip.Hash, Coinbase(101), winner));

        Assert.Equal(new[] { parent.Txid, child.Txid }, removed);
        Assert.Equal(0, _mempool.Count);
    }

    [Fact]
    public void Adding_a_replacement_core_accepted_evicts_the_original_and_its_descendants()
    {
        var original = Pay(_coin, 49 * Btc);
        var child = Pay(new OutPoint(original.Txid, 0), 48 * Btc);
        _mempool.Add(original);
        _mempool.Add(child);
        var replacement = Pay(_coin, 45 * Btc);

        Assert.True(_mempool.Add(replacement));

        Assert.Equal(replacement.Txid, Assert.Single(_mempool.Transactions).Txid);
        Assert.Equal(replacement.Txid, _mempool.SpenderOf(_coin));
        Assert.False(_mempool.Add(replacement));
    }

    [Fact]
    public void A_disconnected_blocks_transactions_return_to_the_mempool_without_its_coinbase()
    {
        var payment = Pay(_coin, 49 * Btc);

        _mempool.ReturnFromDisconnectedBlock(MinedBlock(_utxos.Tip.Hash, Coinbase(101), payment));

        Assert.Equal(payment.Txid, Assert.Single(_mempool.Transactions).Txid);
    }
}
