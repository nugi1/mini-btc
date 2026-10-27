using MiniBtc.Application.Abstractions;
using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Transactions;

namespace MiniBtc.Application.Unconfirmed;

/// <summary>
/// Keeps the mempool view level with Core's mempool: a full reload at startup, then Core's ZMQ
/// sequence events. "A" (added) brings only a txid, so the transaction is fetched with
/// getrawtransaction; "R" (removed) needs nothing more. Every event carries Core's mempool sequence
/// number, which orders it against the reload and against the other events.
/// </summary>
public sealed class MempoolSynchronizer(IBitcoinCoreRpc core, MempoolState mempool)
{
    /// <summary>Replaces the view with Core's mempool. Returns how many transactions it holds.</summary>
    public async Task<int> ReloadAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await core.GetRawMempoolAsync(cancellationToken);
        var transactions = new List<Transaction>();
        foreach (var txid in snapshot.Txids)
        {
            if (await TryFetchAsync(txid, cancellationToken) is { } tx) transactions.Add(tx);
        }
        mempool.Reload(transactions, snapshot.MempoolSequence);
        return transactions.Count;
    }

    /// <summary>Core added <paramref name="txid"/> to its mempool. False if skipped (stale, or already gone).</summary>
    public async Task<bool> AddedAsync(UInt256 txid, ulong mempoolSequence, CancellationToken cancellationToken = default)
    {
        if (mempoolSequence <= mempool.MempoolSequence) return false;
        var tx = await TryFetchAsync(txid, cancellationToken);
        return mempool.Apply(mempoolSequence, view =>
        {
            if (tx is not null) view.Add(tx);
        }) && tx is not null;
    }

    /// <summary>Core removed <paramref name="txid"/> from its mempool (replaced, expired, evicted, or conflicted by a block).</summary>
    public bool Removed(UInt256 txid, ulong mempoolSequence) =>
        mempool.Apply(mempoolSequence, view => view.Remove(txid));

    /// <summary>The transaction, or null when Core no longer has it (removed between the event and the fetch).</summary>
    private async Task<Transaction?> TryFetchAsync(UInt256 txid, CancellationToken cancellationToken)
    {
        try
        {
            return Transaction.Parse(await core.GetRawTransactionAsync(txid, cancellationToken));
        }
        catch (BitcoinCoreRpcException ex) when (ex.Code == RpcErrorCodes.InvalidAddressOrKey)
        {
            return null;
        }
    }
}
