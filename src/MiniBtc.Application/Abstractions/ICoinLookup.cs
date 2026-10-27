using MiniBtc.Domain.Transactions;
using MiniBtc.Domain.Utxo;

namespace MiniBtc.Application.Abstractions;

/// <summary>Looks up specific coins in the stored UTXO set.</summary>
public interface ICoinLookup
{
    /// <summary>
    /// The coins that exist among <paramref name="outPoints"/> (absent ones are left out) and the tip
    /// they reflect, read from one consistent snapshot.
    /// </summary>
    Task<CoinSnapshot> GetCoinsAsync(IReadOnlyCollection<OutPoint> outPoints, CancellationToken cancellationToken = default);
}

public sealed record CoinSnapshot(int TipHeight, IReadOnlyDictionary<OutPoint, Coin> Coins);
