using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Transactions;
using Patterns.DDD;

namespace MiniBtc.Domain.Validation;

/// <summary>
/// Core: CheckTransaction (consensus/tx_check.cpp). The checks a transaction must pass on its own,
/// before anyone looks at the coins it spends, in Core's order.
/// </summary>
public static class TransactionRules
{
    public static IRule<Transaction> All { get; } = Rules.All<Transaction>(
        new HasInputsAndOutputsRule(),
        new SizeLimitRule(),
        new OutputValuesRule(),
        new NoDuplicateInputsRule(),
        new CoinbaseOrRealPrevoutsRule());

    /// <summary>CheckBlock runs CheckTransaction on every transaction of the block, in order.</summary>
    public static IRule<BlockContext> EachInBlock { get; } =
        Rules.ForEach<BlockContext, Transaction>(context => context.Block.Transactions, All);
}

public sealed class HasInputsAndOutputsRule : IRule<Transaction>
{
    public Result Check(Transaction tx)
    {
        if (tx.Inputs.Count == 0) return new Error("bad-txns-vin-empty", $"tx {tx.Txid} has no inputs");
        if (tx.Outputs.Count == 0) return new Error("bad-txns-vout-empty", $"tx {tx.Txid} has no outputs");
        return Result.Success();
    }
}

/// <summary>
/// Without its witness, a transaction must fit in a block: stripped size × 4 ≤ MAX_BLOCK_WEIGHT.
/// The witness isn't counted yet because it hasn't been checked for malleability at this point.
/// </summary>
public sealed class SizeLimitRule : IRule<Transaction>
{
    public Result Check(Transaction tx) =>
        (long)tx.ToBytes(includeWitness: false).Length * ConsensusConstants.WitnessScaleFactor > ConsensusConstants.MaxBlockWeight
            ? new Error("bad-txns-oversize", $"tx {tx.Txid} is larger than a block")
            : Result.Success();
}

/// <summary>
/// Every output value, and their running total, must lie in [0, MAX_MONEY]. This is CVE-2010-5139:
/// in August 2010 two outputs of 92 billion BTC each overflowed the total and minted 184 billion BTC.
/// </summary>
public sealed class OutputValuesRule : IRule<Transaction>
{
    public Result Check(Transaction tx)
    {
        long total = 0;
        foreach (var output in tx.Outputs)
        {
            if (output.Value < 0) return new Error("bad-txns-vout-negative", $"tx {tx.Txid} has a negative output");
            if (output.Value > ConsensusConstants.MaxMoney) return new Error("bad-txns-vout-toolarge", $"tx {tx.Txid} has an output above MAX_MONEY");
            total += output.Value; // both terms ≤ MAX_MONEY, so this cannot overflow a long
            if (!ConsensusConstants.IsMoneyRange(total))
                return new Error("bad-txns-txouttotal-toolarge", $"tx {tx.Txid} outputs total more than MAX_MONEY");
        }
        return Result.Success();
    }
}

/// <summary>
/// No outpoint twice in one transaction (CVE-2018-17144). The coin lookup alone wouldn't catch it:
/// each input finds the same, still unspent coin, and the tx would spend it twice.
/// </summary>
public sealed class NoDuplicateInputsRule : IRule<Transaction>
{
    public Result Check(Transaction tx)
    {
        var seen = new HashSet<OutPoint>();
        foreach (var input in tx.Inputs)
        {
            if (!seen.Add(input.PrevOut))
                return new Error("bad-txns-inputs-duplicate", $"tx {tx.Txid} spends {input.PrevOut} twice");
        }
        return Result.Success();
    }
}

/// <summary>
/// A coinbase's scriptSig must be 2–100 bytes. Any other transaction must not use the null prevout,
/// which only a coinbase may (it is how a coinbase is recognised).
/// </summary>
public sealed class CoinbaseOrRealPrevoutsRule : IRule<Transaction>
{
    public Result Check(Transaction tx)
    {
        if (tx.IsCoinbase)
        {
            var length = tx.Inputs[0].ScriptSig.Length;
            return length is < 2 or > 100
                ? new Error("bad-cb-length", $"coinbase scriptSig is {length} bytes, outside 2–100")
                : Result.Success();
        }

        return tx.Inputs.Any(input => input.PrevOut.IsNull)
            ? new Error("bad-txns-prevout-null", $"tx {tx.Txid} has a null prevout but is not a coinbase")
            : Result.Success();
    }
}
