using System.Data;
using Microsoft.EntityFrameworkCore;
using MiniBtc.Application.Abstractions;
using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Transactions;
using MiniBtc.Domain.Utxo;

namespace MiniBtc.Infrastructure.Persistence;

/// <summary>
/// Reads coins by script from the utxos table (indexed on script_pubkey). The tip and the coins
/// come from one REPEATABLE READ transaction: PostgreSQL takes its snapshot at the first query, so
/// both reads see the database as of the same committed block.
/// </summary>
public sealed class AddressIndex(MiniBtcDbContext db, NetworkParams network) : IAddressIndex
{
    public async Task<IndexedCoins> GetCoinsAsync(byte[] scriptPubKey, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);

        var tipHeight = await db.ChainStates.AsNoTracking()
            .Where(s => s.Network == network.Name)
            .Select(s => (int?)s.TipHeight)
            .SingleOrDefaultAsync(cancellationToken) ?? 0;

        var rows = await db.Coins.AsNoTracking()
            .Where(c => c.ScriptPubKey == scriptPubKey)
            .OrderBy(c => c.Height).ThenBy(c => c.Txid).ThenBy(c => c.Vout)
            .ToListAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        var coins = rows
            .Select(r => new KeyValuePair<OutPoint, Coin>(
                new OutPoint(new UInt256(r.Txid), (uint)r.Vout),
                new Coin(new TxOut(r.Value, r.ScriptPubKey), r.Height, r.IsCoinbase)))
            .ToList();
        return new IndexedCoins(tipHeight, coins);
    }
}
