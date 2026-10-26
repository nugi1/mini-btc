namespace MiniBtc.Domain.Utxo;

/// <summary>
/// Everything a block destroyed, so the block can be disconnected again in a reorg (Core:
/// CBlockUndo in undo.h). Outputs a block created are easy to remove, since the block lists them.
/// The coins it spent are gone from the UTXO set and from the block, which holds only outpoints;
/// this record keeps them.
/// </summary>
/// <remarks>One <see cref="TxUndo"/> per transaction except the coinbase, in block order.</remarks>
public sealed class BlockUndo(IEnumerable<TxUndo> transactions)
{
    public IReadOnlyList<TxUndo> Transactions { get; } = [.. transactions];
}

/// <summary>The coins one transaction spent, one per input, in input order (Core: CTxUndo).</summary>
public sealed class TxUndo(IEnumerable<Coin> spentCoins)
{
    public IReadOnlyList<Coin> SpentCoins { get; } = [.. spentCoins];
}
