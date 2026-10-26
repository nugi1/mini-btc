using MiniBtc.Domain.Blocks;
using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Serialization;
using Patterns.DDD;

namespace MiniBtc.Domain.Validation;

/// <summary>
/// Rules about the block as a whole. <see cref="Structure"/> is CheckBlock's part, run after the
/// merkle check and before CheckTransaction. <see cref="CoinbaseAmount"/> is ConnectBlock's last
/// check, once every transaction's fee is known.
/// </summary>
public static class BlockRules
{
    public static IRule<BlockContext> Structure { get; } = Rules.All<BlockContext>(
        new BlockSizeLimitsRule(),
        new SingleLeadingCoinbaseRule());

    public static IRule<CoinbaseContext> CoinbaseAmount { get; } = new CoinbaseAmountRule();
}

/// <summary>
/// At least one transaction, and the block without witnesses must fit the weight limit
/// (stripped size × 4 ≤ 4,000,000). The full weight, witnesses included, is a contextual
/// check (bad-blk-weight) outside this project's scope.
/// </summary>
public sealed class BlockSizeLimitsRule : IRule<BlockContext>
{
    public Result Check(BlockContext context)
    {
        var transactions = context.Block.Transactions;
        var strippedSize = BlockHeader.Size
            + CompactSize.SizeOf((ulong)transactions.Count)
            + transactions.Sum(tx => (long)tx.ToBytes(includeWitness: false).Length);

        var tooBig = (long)transactions.Count * ConsensusConstants.WitnessScaleFactor > ConsensusConstants.MaxBlockWeight
            || strippedSize * ConsensusConstants.WitnessScaleFactor > ConsensusConstants.MaxBlockWeight;
        return transactions.Count == 0 || tooBig
            ? new Error("bad-blk-length", "size limits failed")
            : Result.Success();
    }
}

/// <summary>
/// The first transaction must be a coinbase and no other may be. There is exactly one place new
/// coins enter a block, and the subsidy rule can only bound what it can find.
/// </summary>
public sealed class SingleLeadingCoinbaseRule : IRule<BlockContext>
{
    public Result Check(BlockContext context)
    {
        var transactions = context.Block.Transactions;
        if (transactions.Count == 0 || !transactions[0].IsCoinbase)
            return new Error("bad-cb-missing", "first tx is not coinbase");
        for (var i = 1; i < transactions.Count; i++)
        {
            if (transactions[i].IsCoinbase)
                return new Error("bad-cb-multiple", "more than one coinbase");
        }
        return Result.Success();
    }
}

/// <summary>The block, its height, and the fees its transactions paid in total.</summary>
public sealed record CoinbaseContext(Block Block, int Height, long Fees, NetworkParams Network)
{
    /// <summary>What the coinbase may claim: the subsidy at this height plus every fee in the block.</summary>
    public long Limit => Fees + Network.GetBlockSubsidy(Height);
}

/// <summary>
/// The coinbase may pay out at most subsidy + fees. Paying less is allowed, and those coins are gone
/// for good. This rule is the 21 million cap, enforced one block at a time.
/// </summary>
public sealed class CoinbaseAmountRule : IRule<CoinbaseContext>
{
    public Result Check(CoinbaseContext context)
    {
        var actual = context.Block.Transactions[0].Outputs.Sum(output => output.Value);
        return actual > context.Limit
            ? new Error("bad-cb-amount", $"coinbase pays too much (actual={actual} vs limit={context.Limit})")
            : Result.Success();
    }
}
