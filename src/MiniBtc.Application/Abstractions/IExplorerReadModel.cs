using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Utxo;

namespace MiniBtc.Application.Abstractions;

/// <summary>
/// The read side of the stored chain, for the explorer: blocks and transactions as stored bytes plus
/// the facts only the database knows (height, the coins each input spent, which outputs are unspent).
/// Decoding the bytes is the application's job, with the domain's own types.
/// </summary>
public interface IExplorerReadModel
{
    /// <summary>The newest blocks of the active chain, highest first, below <paramref name="belowHeight"/> if given.</summary>
    Task<IReadOnlyList<StoredBlockRow>> GetLatestBlocksAsync(int limit, int? belowHeight, CancellationToken cancellationToken = default);

    /// <summary>One block by hash or by height, with its transactions and the coins they spent.</summary>
    Task<StoredBlockDetail?> GetBlockAsync(UInt256? hash, int? height, CancellationToken cancellationToken = default);

    /// <summary>A confirmed transaction, or null if no stored block contains it.</summary>
    Task<StoredTransaction?> GetTransactionAsync(UInt256 txid, CancellationToken cancellationToken = default);
}

/// <param name="Header">The 80 header bytes, as on the wire.</param>
/// <param name="Size">Serialized size in bytes, witnesses included.</param>
/// <param name="Fees">Satoshis.</param>
public sealed record StoredBlockRow(UInt256 Hash, int Height, byte[] Header, int TransactionCount, int Size, long Fees);

/// <param name="NextHash">The block above it in the active chain, if any.</param>
/// <param name="Transactions">Each transaction's bytes, in block order.</param>
/// <param name="SpentCoins">The coin each input spent, by (transaction position, input index).</param>
public sealed record StoredBlockDetail(
    StoredBlockRow Block,
    UInt256? NextHash,
    int TipHeight,
    IReadOnlyList<byte[]> Transactions,
    IReadOnlyDictionary<(int Position, int Input), Coin> SpentCoins);

/// <param name="SpentCoins">One per input, in order; empty for a coinbase.</param>
/// <param name="UnspentOutputs">Indexes of its outputs still in the UTXO set.</param>
public sealed record StoredTransaction(
    byte[] Raw,
    UInt256 BlockHash,
    int BlockHeight,
    int Position,
    int TipHeight,
    IReadOnlyList<Coin> SpentCoins,
    IReadOnlySet<int> UnspentOutputs);
