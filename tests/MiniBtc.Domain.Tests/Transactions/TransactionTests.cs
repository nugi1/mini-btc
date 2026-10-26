using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Serialization;
using MiniBtc.Domain.Tests.TestData;
using MiniBtc.Domain.Transactions;

namespace MiniBtc.Domain.Tests.Transactions;

public class TransactionTests
{
    [Fact]
    public void Genesis_coinbase_decodes_and_its_txid_is_the_merkle_root()
    {
        var tx = Transaction.Parse(RegtestGenesis.Coinbase);

        Assert.Equal(RegtestGenesis.MerkleRoot, tx.Txid.ToString());
        Assert.True(tx.IsCoinbase);
        Assert.Equal(1u, tx.Version);
        Assert.True(Assert.Single(tx.Inputs).PrevOut.IsNull);
        Assert.Equal(TxIn.SequenceFinal, tx.Inputs[0].Sequence);
        Assert.Equal(5_000_000_000L, Assert.Single(tx.Outputs).Value);
        Assert.Equal(0u, tx.LockTime);
        Assert.Equal(RegtestGenesis.Coinbase, tx.ToBytes());
    }

    [Fact]
    public void Standard_p2pkh_payment_decodes_with_the_right_txid()
    {
        var tx = Transaction.Parse(Convert.FromHexString(LegacyTransactions.StandardHex));

        Assert.Equal(LegacyTransactions.StandardTxid, tx.Txid.ToString());
        Assert.False(tx.IsCoinbase);

        var input = Assert.Single(tx.Inputs);
        // Wire order f1fe…fe, so the txid people would type is fefe…f1.
        Assert.Equal(UInt256.Parse(string.Concat(Enumerable.Repeat("fe", 31)) + "f1"), input.PrevOut.Txid);
        Assert.Equal(0u, input.PrevOut.Index);
        Assert.Equal(107, input.ScriptSig.Length);

        var output = Assert.Single(tx.Outputs);
        Assert.Equal(100_000L, output.Value);
        Assert.Equal("76a914c42e7ef92fdb603af844d064faad95db9bcdfd3d88ac", Convert.ToHexStringLower(output.ScriptPubKey.Span));
    }

    [Fact]
    public void Malformed_scripts_are_opaque_bytes_and_round_trip()
    {
        var bytes = Convert.FromHexString(LegacyTransactions.MalformedScriptsHex);

        var tx = Transaction.Parse(bytes);

        Assert.Equal(LegacyTransactions.MalformedScriptsTxid, tx.Txid.ToString());
        Assert.Equal(8, tx.Outputs.Count);
        Assert.Equal("4c", Convert.ToHexStringLower(tx.Outputs[2].ScriptPubKey.Span)); // OP_PUSHDATA1 with no length
        Assert.Equal(bytes, tx.ToBytes());
    }

    [Fact]
    public void Leftover_bytes_fail_like_sendrawtransaction()
    {
        byte[] bytes = [.. RegtestGenesis.Coinbase, 0x00];

        var ex = Assert.Throws<DecodeException>(() => Transaction.Parse(bytes));
        Assert.Equal(DecodeReasons.TxDecodeFailed, ex.Reason);
    }

    [Fact]
    public void Truncated_transaction_is_end_of_data()
    {
        var bytes = RegtestGenesis.Coinbase[..^1]; // locktime cut short

        var ex = Assert.Throws<DecodeException>(() => Transaction.Parse(bytes));
        Assert.Equal(DecodeReasons.EndOfData, ex.Reason);
    }

    [Fact]
    public void A_huge_input_count_hits_end_of_data_instead_of_allocating()
    {
        // version 1, then a count of 0x02000000 inputs (exactly MAX_SIZE) and nothing after it.
        var bytes = Convert.FromHexString("01000000" + "fe00000002");

        var ex = Assert.Throws<DecodeException>(() => Transaction.Parse(bytes));
        Assert.Equal(DecodeReasons.EndOfData, ex.Reason);
    }

    [Fact]
    public void Built_transaction_serializes_to_the_bytes_it_was_decoded_from()
    {
        var original = Transaction.Parse(Convert.FromHexString(LegacyTransactions.StandardHex));

        var rebuilt = new Transaction(
            original.Version,
            original.Inputs.Select(i => new TxIn(i.PrevOut, i.ScriptSig.Span, i.Sequence)),
            original.Outputs.Select(o => new TxOut(o.Value, o.ScriptPubKey.Span)),
            original.LockTime);

        Assert.Equal(original.Txid, rebuilt.Txid);
        Assert.Equal(original.ToBytes(), rebuilt.ToBytes());
    }
}
