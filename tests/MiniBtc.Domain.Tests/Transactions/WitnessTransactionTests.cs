using MiniBtc.Domain.Serialization;
using MiniBtc.Domain.Tests.TestData;
using MiniBtc.Domain.Transactions;

namespace MiniBtc.Domain.Tests.Transactions;

public class WitnessTransactionTests
{
    private static byte[] Hex(string hex) => Convert.FromHexString(hex);

    [Fact]
    public void P2wpkh_spend_decodes_with_separate_txid_and_wtxid()
    {
        var bytes = Hex(WitnessTransactions.P2wpkhHex);

        var tx = Transaction.Parse(bytes);

        Assert.True(tx.HasWitness);
        Assert.Equal(WitnessTransactions.P2wpkhTxid, tx.Txid.ToString());
        Assert.Equal(WitnessTransactions.P2wpkhWtxid, tx.Wtxid.ToString());

        var input = Assert.Single(tx.Inputs);
        Assert.True(input.ScriptSig.IsEmpty);                 // native SegWit: unlocking data moved out
        Assert.Equal(2, input.Witness.Items.Count);
        Assert.Equal(72, input.Witness.Items[0].Length);       // DER signature + sighash byte
        Assert.Equal(0x01, input.Witness.Items[0].Span[^1]);   // SIGHASH_ALL
        Assert.Equal(33, input.Witness.Items[1].Length);       // compressed pubkey
        Assert.Equal(60_000L, Assert.Single(tx.Outputs).Value);

        Assert.Equal(bytes, tx.ToBytes());
        Assert.Equal(Hex(WitnessTransactions.P2wpkhStrippedHex), tx.ToBytes(includeWitness: false));
    }

    [Fact]
    public void Txid_ignores_the_witness_wtxid_does_not()
    {
        // Same tx without its witness: identical txid (signatures can't malleate it) but no witness,
        // so its wtxid falls back to the txid and no longer matches the signed tx's wtxid.
        var stripped = Transaction.Parse(Hex(WitnessTransactions.P2wpkhStrippedHex));

        Assert.False(stripped.HasWitness);
        Assert.Equal(WitnessTransactions.P2wpkhTxid, stripped.Txid.ToString());
        Assert.Equal(stripped.Txid, stripped.Wtxid);
        Assert.NotEqual(WitnessTransactions.P2wpkhWtxid, stripped.Wtxid.ToString());
    }

    [Fact]
    public void Witness_coinbase_decodes_with_reserved_value_and_commitment()
    {
        var bytes = Hex(WitnessTransactions.CoinbaseHex);

        var tx = Transaction.Parse(bytes);

        Assert.True(tx.IsCoinbase);
        Assert.Equal(2u, tx.Version);
        Assert.Equal(WitnessTransactions.CoinbaseTxid, tx.Txid.ToString());
        Assert.Equal(WitnessTransactions.CoinbaseWtxid, tx.Wtxid.ToString());

        var reserved = Assert.Single(Assert.Single(tx.Inputs).Witness.Items);
        Assert.Equal(new byte[32], reserved.ToArray());        // BIP141 witness reserved value

        Assert.Equal(2, tx.Outputs.Count);
        Assert.StartsWith("6a24aa21a9ed", Convert.ToHexStringLower(tx.Outputs[1].ScriptPubKey.Span)); // OP_RETURN commitment
        Assert.Equal(bytes, tx.ToBytes());
    }

    [Fact]
    public void Legacy_transaction_has_one_encoding_and_wtxid_equals_txid()
    {
        var bytes = Hex(LegacyTransactions.StandardHex);

        var tx = Transaction.Parse(bytes);

        Assert.False(tx.HasWitness);
        Assert.Equal(tx.Txid, tx.Wtxid);
        Assert.Equal(bytes, tx.ToBytes(includeWitness: true));  // no marker without witness data
    }

    [Fact]
    public void Witness_flag_with_only_empty_stacks_is_superfluous()
    {
        // The P2WPKH tx in extended format, but with its one witness stack emptied (count 00).
        var legacy = WitnessTransactions.P2wpkhStrippedHex;
        var bytes = Hex(legacy[..8] + "0001" + legacy[8..^8] + "00" + legacy[^8..]);

        var ex = Assert.Throws<DecodeException>(() => Transaction.Parse(bytes));
        Assert.Equal(DecodeReasons.SuperfluousWitness, ex.Reason);
    }

    [Theory]
    [InlineData("02")] // flag without the witness bit
    [InlineData("03")] // witness bit plus an unknown bit
    public void Unknown_flag_bits_are_rejected(string flag)
    {
        var bytes = flag == "02"
            ? Hex(WitnessTransactions.P2wpkhStrippedHex[..8] + "00" + flag + WitnessTransactions.P2wpkhStrippedHex[8..])
            : Hex(WitnessTransactions.P2wpkhHex[..8] + "00" + flag + WitnessTransactions.P2wpkhHex[12..]);

        var ex = Assert.Throws<DecodeException>(() => Transaction.Parse(bytes));
        Assert.Equal(DecodeReasons.UnknownOptionalData, ex.Reason);
    }

    [Fact]
    public void Empty_input_list_is_read_as_the_marker()
    {
        // A legacy-style tx with zero inputs and one output: 00 is taken as the marker and 01 as
        // the flag, so the output count is misread as an input count and decoding fails. This is
        // why sendrawtransaction says "Make sure the tx has at least one input."
        var bytes = Hex("01000000" + "00" + "01" + "a086010000000000" + "19" +
                        "76a914c42e7ef92fdb603af844d064faad95db9bcdfd3d88ac" + "00000000");

        var ex = Assert.Throws<DecodeException>(() => Transaction.Parse(bytes));
        Assert.Equal(DecodeReasons.EndOfData, ex.Reason);
    }

    [Fact]
    public void Zero_inputs_and_zero_outputs_decode_as_in_core()
    {
        // vin reads empty, the next byte is 00: no flags, and Core then reads no vout either.
        var tx = Transaction.Parse(Hex("01000000" + "00" + "00" + "00000000"));

        Assert.Empty(tx.Inputs);
        Assert.Empty(tx.Outputs);
        Assert.False(tx.HasWitness);
    }
}
