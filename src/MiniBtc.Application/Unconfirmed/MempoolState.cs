using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Transactions;
using MiniBtc.Domain.Unconfirmed;

namespace MiniBtc.Application.Unconfirmed;

/// <summary>
/// The node's one mempool view, shared by the broadcast gateway, the ZMQ mempool events and chain
/// sync. It lives in memory: Core is the authority on what its mempool holds, and the node reloads
/// it from Core at startup. A lock serializes access, so each read or change sees a whole mempool.
/// </summary>
public sealed class MempoolState(NetworkParams network)
{
    private readonly object _gate = new();
    private readonly Mempool _mempool = new(network);

    /// <summary>
    /// Core's mempool sequence number the view reflects: ZMQ events at or below it are already
    /// included (a reload from getrawmempool covers them), so they are skipped.
    /// </summary>
    public ulong MempoolSequence { get; private set; }

    public T Read<T>(Func<Mempool, T> read)
    {
        lock (_gate) return read(_mempool);
    }

    public void Update(Action<Mempool> update)
    {
        lock (_gate) update(_mempool);
    }

    /// <summary>Applies one numbered event unless an earlier reload already covers it. False if skipped.</summary>
    public bool Apply(ulong mempoolSequence, Action<Mempool> update)
    {
        lock (_gate)
        {
            if (mempoolSequence <= MempoolSequence) return false;
            update(_mempool);
            MempoolSequence = mempoolSequence;
            return true;
        }
    }

    /// <summary>Replaces the whole view with Core's mempool as of <paramref name="mempoolSequence"/>.</summary>
    public void Reload(IEnumerable<Transaction> transactions, ulong mempoolSequence)
    {
        lock (_gate)
        {
            foreach (var tx in _mempool.Transactions.ToList()) _mempool.Remove(tx.Txid);
            foreach (var tx in transactions) _mempool.Add(tx);
            MempoolSequence = mempoolSequence;
        }
    }
}
