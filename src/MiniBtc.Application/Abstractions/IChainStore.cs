using MiniBtc.Domain.Blocks;
using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Utxo;

namespace MiniBtc.Application.Abstractions;

/// <summary>
/// Where the node keeps its chain: the UTXO set and everything each connected block produced.
/// It stages changes; <see cref="Patterns.DDD.Application.IUnitOfWork"/> commits them, so one block's
/// rows, the new tip and its outbox messages land in a single database transaction.
/// </summary>
public interface IChainStore
{
    /// <summary>
    /// Rehydrates the UTXO set. On an empty database it starts at genesis and stages the genesis
    /// block, so the first commit records it.
    /// </summary>
    Task<UtxoSet> LoadAsync(NetworkParams network, CancellationToken cancellationToken = default);

    /// <summary>Stages a block that the loaded set has just connected: its rows, the UTXO change, undo data, the new tip.</summary>
    void AddConnectedBlock(Block block, ConnectedBlock connected);

    /// <summary>
    /// The loaded set's tip block as stored, with the undo data saved when it was connected:
    /// everything <see cref="UtxoSet.Disconnect"/> needs.
    /// </summary>
    Task<StoredBlock> LoadTipBlockAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages a block that the loaded set has just disconnected: its rows and undo data go, the
    /// UTXO change is applied, the tip moves back.
    /// </summary>
    void AddDisconnectedBlock(Block block, DisconnectedBlock disconnected);
}

/// <summary>A connected block read back from storage, with its undo data.</summary>
public sealed record StoredBlock(Block Block, BlockUndo Undo);
