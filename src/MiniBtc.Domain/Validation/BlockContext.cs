using MiniBtc.Domain.Blocks;
using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Utxo;

namespace MiniBtc.Domain.Validation;

/// <summary>What block rules look at: the block, the tip it should extend, and the chain's parameters.</summary>
public sealed record BlockContext(Block Block, ChainTip Tip, NetworkParams Network)
{
    /// <summary>The height the block would have once connected.</summary>
    public int Height => Tip.Height + 1;
}
