using MiniBtc.Domain.Hashing;

namespace MiniBtc.Application.Abstractions;

/// <summary>Totals of the stored UTXO set, the node's side of a gettxoutsetinfo.</summary>
public interface IUtxoStats
{
    /// <summary>The tip and the set's coin count and total value, from one consistent snapshot.</summary>
    Task<UtxoSummary> GetSummaryAsync(CancellationToken cancellationToken = default);
}

/// <param name="TotalAmount">Satoshis.</param>
public sealed record UtxoSummary(int TipHeight, UInt256 TipHash, long CoinCount, long TotalAmount);
