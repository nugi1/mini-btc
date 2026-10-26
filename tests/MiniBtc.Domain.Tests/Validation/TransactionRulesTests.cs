using MiniBtc.Domain.Blocks;
using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Tests.TestData;
using MiniBtc.Domain.Transactions;
using MiniBtc.Domain.Validation;
using static MiniBtc.Domain.Tests.TestData.TestChain;

namespace MiniBtc.Domain.Tests.Validation;

public class TransactionRulesTests
{
    private static readonly OutPoint SomeCoin = new(UInt256.Parse(RegtestGenesis.MerkleRoot), 0);
    private static readonly OutPoint OtherCoin = new(UInt256.Parse(RegtestGenesis.MerkleRoot), 1);

    public static TheoryData<string> CapturedBlocks => [.. RegtestFixtures.Names];

    [Theory]
    [MemberData(nameof(CapturedBlocks))]
    public void Every_transaction_core_mined_passes(string name)
    {
        var block = Block.Parse(RegtestFixtures.Load(name).Raw);

        Assert.All(block.Transactions, tx => Assert.True(TransactionRules.All.Check(tx).IsSuccess));
    }

    [Fact]
    public void No_inputs_is_bad_txns_vin_empty() =>
        AssertRejected("bad-txns-vin-empty", Tx([], [Out(1)]));

    [Fact]
    public void No_outputs_is_bad_txns_vout_empty() =>
        AssertRejected("bad-txns-vout-empty", Tx([In(SomeCoin)], []));

    [Fact]
    public void Larger_than_a_block_without_witness_is_bad_txns_oversize() =>
        AssertRejected("bad-txns-oversize", Tx([In(SomeCoin)], [new TxOut(1, new byte[1_000_000])]));

    [Fact]
    public void Negative_output_is_bad_txns_vout_negative() =>
        AssertRejected("bad-txns-vout-negative", Tx([In(SomeCoin)], [Out(-1)]));

    [Fact]
    public void Output_above_max_money_is_bad_txns_vout_toolarge() =>
        AssertRejected("bad-txns-vout-toolarge", Tx([In(SomeCoin)], [Out(ConsensusConstants.MaxMoney + 1)]));

    [Fact]
    public void Outputs_summing_above_max_money_are_bad_txns_txouttotal_toolarge() =>
        // Each output is legal on its own; together they could mint coins (CVE-2010-5139).
        AssertRejected("bad-txns-txouttotal-toolarge",
            Tx([In(SomeCoin)], [Out(ConsensusConstants.MaxMoney), Out(ConsensusConstants.MaxMoney)]));

    [Fact]
    public void The_same_outpoint_twice_is_bad_txns_inputs_duplicate() =>
        AssertRejected("bad-txns-inputs-duplicate", Tx([In(SomeCoin), In(SomeCoin)], [Out(1)]));

    [Theory]
    [InlineData(1)]
    [InlineData(101)]
    public void Coinbase_scriptsig_outside_2_to_100_bytes_is_bad_cb_length(int length) =>
        AssertRejected("bad-cb-length", Tx([new TxIn(OutPoint.Null, new byte[length])], [Out(1)]));

    [Theory]
    [InlineData(2)]
    [InlineData(100)]
    public void Coinbase_scriptsig_of_2_to_100_bytes_is_fine(int length) =>
        Assert.True(TransactionRules.All.Check(Tx([new TxIn(OutPoint.Null, new byte[length])], [Out(1)])).IsSuccess);

    [Fact]
    public void Null_prevout_in_a_non_coinbase_is_bad_txns_prevout_null() =>
        // Two inputs, so not a coinbase; one of them still uses the coinbase marker.
        AssertRejected("bad-txns-prevout-null", Tx([In(OutPoint.Null), In(OtherCoin)], [Out(1)]));

    [Fact]
    public void Checks_run_in_core_order()
    {
        // Negative output and duplicate inputs: the output check comes first in CheckTransaction.
        AssertRejected("bad-txns-vout-negative", Tx([In(SomeCoin), In(SomeCoin)], [Out(-1)]));
    }

    private static Transaction Tx(TxIn[] inputs, TxOut[] outputs) => new(2, inputs, outputs, 0);
    private static TxIn In(OutPoint prevOut) => new(prevOut, ReadOnlySpan<byte>.Empty);
    private static TxOut Out(long value) => new(value, AnyoneCanSpend);

    private static void AssertRejected(string code, Transaction tx)
    {
        var result = TransactionRules.All.Check(tx);
        Assert.True(result.IsFailure, $"expected {code}, but the transaction passed");
        Assert.Equal(code, result.Error!.Code);
    }
}
