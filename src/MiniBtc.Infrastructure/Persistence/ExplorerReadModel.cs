using System.Data;
using Microsoft.EntityFrameworkCore;
using MiniBtc.Application.Abstractions;
using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Transactions;
using MiniBtc.Domain.Utxo;

namespace MiniBtc.Infrastructure.Persistence;

/// <summary>
/// The explorer's reads, straight from the tables the sync writes. Each method reads inside one
/// REPEATABLE READ transaction, so a block committed meanwhile can't mix two states into one answer.
/// </summary>
public sealed class ExplorerReadModel(MiniBtcDbContext db, NetworkParams network) : IExplorerReadModel
{
    public async Task<IReadOnlyList<StoredBlockRow>> GetLatestBlocksAsync(int limit, int? belowHeight, CancellationToken cancellationToken = default)
    {
        var query = db.Blocks.AsNoTracking();
        if (belowHeight is { } below) query = query.Where(b => b.Height < below);
        var rows = await query.OrderByDescending(b => b.Height).Take(limit).ToListAsync(cancellationToken);
        return rows.Select(ToRow).ToList();
    }

    public async Task<StoredBlockDetail?> GetBlockAsync(UInt256? hash, int? height, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);

        BlockRecord? block;
        if (hash is { } h)
        {
            var key = h.ToWireBytes();
            block = await db.Blocks.AsNoTracking().SingleOrDefaultAsync(b => b.Hash == key, cancellationToken);
        }
        else
        {
            block = await db.Blocks.AsNoTracking().SingleOrDefaultAsync(b => b.Height == height, cancellationToken);
        }
        if (block is null) return null;

        var next = await db.Blocks.AsNoTracking().Where(b => b.Height == block.Height + 1).Select(b => b.Hash).SingleOrDefaultAsync(cancellationToken);
        var transactions = await db.Transactions.AsNoTracking()
            .Where(t => t.BlockHash == block.Hash).OrderBy(t => t.Position).Select(t => t.Raw).ToListAsync(cancellationToken);
        var undo = await db.UndoCoins.AsNoTracking().Where(u => u.BlockHash == block.Hash).ToListAsync(cancellationToken);
        var tipHeight = await TipHeightAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new StoredBlockDetail(
            ToRow(block),
            next is null ? null : new UInt256(next),
            tipHeight,
            transactions,
            undo.ToDictionary(u => (u.TxPosition, u.InputIndex), ToCoin));
    }

    public async Task<StoredTransaction?> GetTransactionAsync(UInt256 txid, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);

        var key = txid.ToWireBytes();
        var row = await db.Transactions.AsNoTracking().FirstOrDefaultAsync(t => t.Txid == key, cancellationToken);
        if (row is null) return null;

        var height = await db.Blocks.AsNoTracking().Where(b => b.Hash == row.BlockHash).Select(b => b.Height).SingleAsync(cancellationToken);
        var spent = await db.UndoCoins.AsNoTracking()
            .Where(u => u.BlockHash == row.BlockHash && u.TxPosition == row.Position)
            .OrderBy(u => u.InputIndex).ToListAsync(cancellationToken);
        var unspent = await db.Coins.AsNoTracking().Where(c => c.Txid == key).Select(c => c.Vout).ToListAsync(cancellationToken);
        var tipHeight = await TipHeightAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new StoredTransaction(
            row.Raw, new UInt256(row.BlockHash), height, row.Position, tipHeight,
            spent.Select(ToCoin).ToList(), unspent.ToHashSet());
    }

    private async Task<int> TipHeightAsync(CancellationToken cancellationToken) =>
        await db.ChainStates.AsNoTracking().Where(s => s.Network == network.Name)
            .Select(s => (int?)s.TipHeight).SingleOrDefaultAsync(cancellationToken) ?? 0;

    private static StoredBlockRow ToRow(BlockRecord b) =>
        new(new UInt256(b.Hash), b.Height, b.Header, b.TransactionCount, b.Size, b.Fees);

    private static Coin ToCoin(UndoCoinRecord u) => new(new TxOut(u.Value, u.ScriptPubKey), u.Height, u.IsCoinbase);
}
