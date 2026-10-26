using MiniBtc.Domain.Utxo;

namespace MiniBtc.Application.ChainSync;

/// <summary>What one sync run did.</summary>
/// <param name="ConnectedBlocks">Blocks validated, applied and committed in this run.</param>
/// <param name="Tip">Our tip when the run ended.</param>
/// <param name="CoreHeight">Core's tip height as last seen.</param>
public sealed record SyncReport(int ConnectedBlocks, ChainTip Tip, int CoreHeight)
{
    public bool CaughtUp => Tip.Height >= CoreHeight;
}

/// <summary>Reject codes of a sync run that are ours, not Core's.</summary>
public static class SyncErrors
{
    /// <summary>Core is on another network than the one we validate.</summary>
    public const string ChainMismatch = "chain-mismatch";

    /// <summary>Core's block 0 is not the genesis our NetworkParams build.</summary>
    public const string GenesisMismatch = "genesis-mismatch";

    /// <summary>Our decoder could not read bytes Core served as a block.</summary>
    public const string UndecodableBlock = "undecodable-block";

    /// <summary>Core served a block whose hash is not the one we asked for.</summary>
    public const string BlockHashMismatch = "block-hash-mismatch";
}
