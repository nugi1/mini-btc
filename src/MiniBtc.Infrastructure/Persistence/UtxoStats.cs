using System.Data;
using Microsoft.EntityFrameworkCore;
using MiniBtc.Application.Abstractions;
using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Hashing;

namespace MiniBtc.Infrastructure.Persistence;

/// <summary>Counts and sums the utxos table, with the tip, in one REPEATABLE READ snapshot.</summary>
public sealed class UtxoStats(MiniBtcDbContext db, NetworkParams network) : IUtxoStats
{
    public async Task<UtxoSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var state = await db.ChainStates.AsNoTracking().SingleOrDefaultAsync(s => s.Network == network.Name, cancellationToken);
        var count = await db.Coins.LongCountAsync(cancellationToken);
        var total = count == 0 ? 0 : await db.Coins.SumAsync(c => c.Value, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return state is null
            ? new UtxoSummary(0, network.GenesisHash, 0, 0)
            : new UtxoSummary(state.TipHeight, new UInt256(state.TipHash), count, total);
    }
}
