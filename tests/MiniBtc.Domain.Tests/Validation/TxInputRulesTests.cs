using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Tests.TestData;
using MiniBtc.Domain.Transactions;
using MiniBtc.Domain.Utxo;
using MiniBtc.Domain.Validation;
using static MiniBtc.Domain.Tests.TestData.TestChain;

namespace MiniBtc.Domain.Tests.Validation;

public class TxInputRulesTests
{
    private const long Btc = ConsensusConstants.Coin;
    private static readonly UInt256 SomeTxid = UInt256.Parse(RegtestGenesis.MerkleRoot);

    [Fact]
    public void A_missing_coin_is_bad_txns_inputs_missingorspent() =>
        AssertRejected("bad-txns-inputs-missingorspent", Spending(10 * Btc, height: 200, Regular(10 * Btc, 5), null));

    [Theory]
    [InlineData(1, 100, false)]  // depth 99
    [InlineData(1, 101, true)]   // depth 100
    [InlineData(150, 249, false)]
    [InlineData(150, 250, true)]
    public void A_coinbase_coin_needs_100_confirmations(int coinHeight, int spendHeight, bool allowed)
    {
        var context = Spending(1 * Btc, spendHeight, new Coin(Out(50 * Btc), coinHeight, IsCoinbase: true));

        var result = TxInputRules.All.Check(context);

        Assert.Equal(allowed, result.IsSuccess);
        if (!allowed) Assert.Equal("bad-txns-premature-spend-of-coinbase", result.Error!.Code);
    }

    [Fact]
    public void A_regular_coin_is_spendable_in_the_next_block() =>
        Assert.True(TxInputRules.All.Check(Spending(1 * Btc, height: 6, Regular(5 * Btc, 5))).IsSuccess);

    [Fact]
    public void An_input_value_out_of_range_is_bad_txns_inputvalues_outofrange() =>
        AssertRejected("bad-txns-inputvalues-outofrange",
            Spending(1, height: 10, Regular(ConsensusConstants.MaxMoney + 1, 5)));

    [Fact]
    public void Input_values_summing_above_max_money_are_bad_txns_inputvalues_outofrange() =>
        AssertRejected("bad-txns-inputvalues-outofrange",
            Spending(1, height: 10, Regular(ConsensusConstants.MaxMoney, 5), Regular(ConsensusConstants.MaxMoney, 5)));

    [Fact]
    public void Spending_more_than_the_inputs_is_bad_txns_in_belowout() =>
        AssertRejected("bad-txns-in-belowout", Spending(11 * Btc, height: 10, Regular(10 * Btc, 5)));

    [Fact]
    public void Outputs_equal_to_inputs_is_a_zero_fee_and_allowed()
    {
        var context = Spending(10 * Btc, height: 10, Regular(10 * Btc, 5));

        Assert.True(TxInputRules.All.Check(context).IsSuccess);
        Assert.Equal(0, TxInputRules.Fee(context));
    }

    [Fact]
    public void Fee_is_inputs_minus_outputs()
    {
        var context = Spending(9 * Btc, height: 10, Regular(6 * Btc, 5), Regular(4 * Btc, 5));

        Assert.Equal(1 * Btc, TxInputRules.Fee(context));
    }

    private static TxOut Out(long value) => new(value, AnyoneCanSpend);
    private static Coin Regular(long value, int height) => new(Out(value), height, IsCoinbase: false);

    /// <summary>A tx with one input per coin and one output of <paramref name="outputValue"/>.</summary>
    private static TxSpendContext Spending(long outputValue, int height, params Coin?[] coins)
    {
        var inputs = coins.Select((_, i) => new TxIn(new OutPoint(SomeTxid, (uint)i), ReadOnlySpan<byte>.Empty));
        var tx = new Transaction(2, inputs, [Out(outputValue)], 0);
        return new TxSpendContext(tx, coins, height);
    }

    private static void AssertRejected(string code, TxSpendContext context)
    {
        var result = TxInputRules.All.Check(context);
        Assert.True(result.IsFailure, $"expected {code}, but the inputs passed");
        Assert.Equal(code, result.Error!.Code);
    }
}
