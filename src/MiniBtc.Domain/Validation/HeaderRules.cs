using MiniBtc.Domain.Blocks;
using MiniBtc.Domain.Hashing;
using Patterns.DDD;

namespace MiniBtc.Domain.Validation;

/// <summary>
/// Checks on the header and on how the header commits to the transactions, in the order Core runs
/// them: AcceptBlockHeader (proof of work, then the parent), then CheckBlock's merkle check.
/// Rules.All stops at the first failure, so a block breaking several rules gets the same reject
/// reason Core would report.
/// </summary>
public static class HeaderRules
{
    public static IRule<BlockContext> All { get; } = Rules.All<BlockContext>(
        new ProofOfWorkRule(),
        new BuildsOnTipRule(),
        new MerkleRootRule());
}

/// <summary>
/// Core: CheckProofOfWork. The bits must decode to a valid target (not negative, not zero, no
/// overflow, not above powLimit), and the block hash, read as a number, must not exceed it.
/// Both failures share Core's reason.
/// </summary>
public sealed class ProofOfWorkRule : IRule<BlockContext>
{
    public Result Check(BlockContext context)
    {
        var header = context.Block.Header;
        var target = CompactTarget.Decode(header.Bits, out var negative, out var overflow);

        var validTarget = !negative && !overflow && target != UInt256.Zero && target <= context.Network.PowLimit;
        return validTarget && header.Hash <= target
            ? Result.Success()
            : new Error("high-hash", "proof of work failed");
    }
}

/// <summary>
/// The block's parent must be our tip. Core reports an unknown parent as prev-blk-not-found. Our
/// node follows Core's active chain, which it knows only through its tip, so a block on any other
/// parent means Core has switched branches: the caller takes the reorg path on this code.
/// </summary>
public sealed class BuildsOnTipRule : IRule<BlockContext>
{
    public Result Check(BlockContext context) =>
        context.Block.Header.PreviousBlockHash == context.Tip.Hash
            ? Result.Success()
            : new Error(HeaderErrors.PrevBlockNotFound,
                $"block {context.Block.Hash} builds on {context.Block.Header.PreviousBlockHash}, not on the tip {context.Tip.Hash}");
}

/// <summary>
/// Core: CheckMerkleRoot. The root computed from the txids must equal the header's, which is what
/// ties the transactions to the proof of work. Then the CVE-2012-2459 check: a tx list whose root
/// only matches thanks to a duplicated last hash is rejected without marking the real block invalid.
/// </summary>
public sealed class MerkleRootRule : IRule<BlockContext>
{
    public Result Check(BlockContext context)
    {
        var root = context.Block.ComputeMerkleRoot(out var mutated);
        if (root != context.Block.Header.MerkleRoot)
            return new Error("bad-txnmrklroot", "hashMerkleRoot mismatch");
        if (mutated)
            return new Error("bad-txns-duplicate", "duplicate transaction");
        return Result.Success();
    }
}

/// <summary>Header reject codes other code reacts to.</summary>
public static class HeaderErrors
{
    /// <summary>Core's code for a block whose parent it doesn't have (validation.cpp AcceptBlockHeader).</summary>
    public const string PrevBlockNotFound = "prev-blk-not-found";
}
