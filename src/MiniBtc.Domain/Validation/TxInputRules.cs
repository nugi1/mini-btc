using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Transactions;
using MiniBtc.Domain.Utxo;
using Patterns.DDD;

namespace MiniBtc.Domain.Validation;

/// <summary>
/// A non-coinbase transaction with the coins its inputs resolve to (null where no unspent coin
/// exists), at the height of the block that would include it.
/// </summary>
public sealed record TxSpendContext(Transaction Transaction, IReadOnlyList<Coin?> SpentCoins, int SpendHeight);

/// <summary>
/// Core: Consensus::CheckTxInputs (consensus/tx_verify.cpp). The checks that need the coins being
/// spent, in Core's order. Scripts (who may spend) are checked separately.
/// </summary>
public static class TxInputRules
{
    public static IRule<TxSpendContext> All { get; } = Rules.All<TxSpendContext>(
        new InputsAvailableRule(),
        new SpentCoinsRule(),
        new ValueBalanceRule());

    /// <summary>The fee: inputs minus outputs. Valid only after <see cref="All"/> passed.</summary>
    public static long Fee(TxSpendContext context) =>
        context.SpentCoins.Sum(coin => coin!.Value) - context.Transaction.Outputs.Sum(output => output.Value);
}

/// <summary>Every input must name a coin that exists and is unspent.</summary>
public sealed class InputsAvailableRule : IRule<TxSpendContext>
{
    public Result Check(TxSpendContext context) =>
        context.SpentCoins.Any(coin => coin is null)
            ? new Error("bad-txns-inputs-missingorspent", "CheckTxInputs: inputs missing/spent")
            : Result.Success();
}

/// <summary>
/// Per input, in order: a coinbase coin must be at least COINBASE_MATURITY deep, and every input
/// value, and their running total, must lie in [0, MAX_MONEY]. Maturity exists because a reorg can
/// erase a coinbase, and everything spending it would vanish too. 100 blocks make that practically
/// impossible.
/// </summary>
public sealed class SpentCoinsRule : IRule<TxSpendContext>
{
    public Result Check(TxSpendContext context)
    {
        long valueIn = 0;
        foreach (var coin in context.SpentCoins)
        {
            var depth = context.SpendHeight - coin!.Height;
            if (coin.IsCoinbase && depth < ConsensusConstants.CoinbaseMaturity)
                return new Error("bad-txns-premature-spend-of-coinbase", $"tried to spend coinbase at depth {depth}");

            valueIn += coin.Value;
            if (!ConsensusConstants.IsMoneyRange(coin.Value) || !ConsensusConstants.IsMoneyRange(valueIn))
                return new Error("bad-txns-inputvalues-outofrange", "input values out of range");
        }
        return Result.Success();
    }
}

/// <summary>
/// Inputs must cover outputs; the difference is the fee the miner collects. Without this rule
/// a transaction could create coins from nothing.
/// </summary>
public sealed class ValueBalanceRule : IRule<TxSpendContext>
{
    public Result Check(TxSpendContext context)
    {
        var valueIn = context.SpentCoins.Sum(coin => coin!.Value);
        var valueOut = context.Transaction.Outputs.Sum(output => output.Value);
        if (valueIn < valueOut)
            return new Error("bad-txns-in-belowout", $"value in ({valueIn} sat) < value out ({valueOut} sat)");
        if (!ConsensusConstants.IsMoneyRange(valueIn - valueOut))
            return new Error("bad-txns-fee-outofrange", "fee out of range");
        return Result.Success();
    }
}
