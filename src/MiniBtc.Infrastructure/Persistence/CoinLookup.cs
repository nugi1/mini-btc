using System.Data;
using Microsoft.EntityFrameworkCore;
using MiniBtc.Application.Abstractions;
using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Transactions;
using MiniBtc.Domain.Utxo;

namespace MiniBtc.Infrastructure.Persistence;

/// <summary>
/// Reads named coins by primary key (txid, vout). The tip and the coins come from one REPEATABLE READ
/// snapshot, like <see cref="AddressIndex"/>, so a block committed meanwhile can't split the answer.
/// A transaction has a handful of inputs, so one key lookup each is cheaper than anything clever.
/// </summary>
public sealed class CoinLookup(MiniBtcDbContext db, NetworkParams network) : ICoinLookup
{
    public async Task<CoinSnapshot> GetCoinsAsync(IReadOnlyCollection<OutPoint> outPoints, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);

        var tipHeight = await db.ChainStates.AsNoTracking()
            .Where(s => s.Network == network.Name)
            .Select(s => (int?)s.TipHeight)
            .SingleOrDefaultAsync(cancellationToken) ?? 0;

        var coins = new Dictionary<OutPoint, Coin>();
        foreach (var outPoint in outPoints)
        {
            var txid = outPoint.Txid.ToWireBytes();
            var vout = (int)outPoint.Index;
            var row = await db.Coins.AsNoTracking()
                .SingleOrDefaultAsync(c => c.Txid == txid && c.Vout == vout, cancellationToken);
            if (row is not null)
                coins[outPoint] = new Coin(new TxOut(row.Value, row.ScriptPubKey), row.Height, row.IsCoinbase);
        }

        await transaction.CommitAsync(cancellationToken);
        return new CoinSnapshot(tipHeight, coins);
    }
}
