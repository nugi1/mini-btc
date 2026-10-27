using MiniBtc.Domain.Transactions;
using MiniBtc.Domain.Utxo;

namespace MiniBtc.Application.Abstractions;

/// <summary>
/// The stored UTXO set, searchable by output script. The node never registers addresses: every
/// coin is indexed by its scriptPubKey, so any address can be asked about at any time.
/// </summary>
public interface IAddressIndex
{
    /// <summary>
    /// The coins locked by <paramref name="scriptPubKey"/> and the tip they reflect, read from one
    /// consistent snapshot, so a block connected meanwhile can't mix two states into one answer.
    /// </summary>
    Task<IndexedCoins> GetCoinsAsync(byte[] scriptPubKey, CancellationToken cancellationToken = default);
}

/// <param name="TipHeight">Height of the tip the coins reflect.</param>
/// <param name="Coins">Ordered by height, then outpoint.</param>
public sealed record IndexedCoins(int TipHeight, IReadOnlyList<KeyValuePair<OutPoint, Coin>> Coins);
