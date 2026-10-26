using MiniBtc.Domain.Utxo;
using Patterns.DDD;

namespace MiniBtc.Infrastructure.Persistence;

// The persistence model: plain rows, kept apart from the domain model. Hashes are stored as their
// 32 bytes in wire order (display order is the reverse; see UInt256).

/// <summary>
/// The tip the stored UTXO set reflects, one row per network. While a UTXO set is loaded it also
/// carries that aggregate's domain events, so Patterns.DDD's outbox interceptor, which collects
/// events from tracked IHasDomainEvents entities, publishes them in the same transaction.
/// </summary>
public sealed class ChainStateRecord : IHasDomainEvents
{
    private UtxoSet? _utxos;

    public string Network { get; set; } = "";
    public byte[] TipHash { get; set; } = [];
    public int TipHeight { get; set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _utxos?.DomainEvents ?? Array.Empty<IDomainEvent>();
    void IHasDomainEvents.ClearDomainEvents() => ((IHasDomainEvents?)_utxos)?.ClearDomainEvents();

    internal void Track(UtxoSet utxos)
    {
        _utxos = utxos;
        SyncTip();
    }

    internal void SyncTip()
    {
        if (_utxos is null) return;
        TipHash = _utxos.Tip.Hash.ToWireBytes();
        TipHeight = _utxos.Tip.Height;
    }
}

public sealed class BlockRecord
{
    public byte[] Hash { get; set; } = [];
    public int Height { get; set; }
    public byte[] PreviousHash { get; set; } = [];
    public byte[] Header { get; set; } = [];
    public int TransactionCount { get; set; }
    public int Size { get; set; }
    public long Fees { get; set; }
}

public sealed class TransactionRecord
{
    public byte[] BlockHash { get; set; } = [];
    public int Position { get; set; }
    public byte[] Txid { get; set; } = [];
    public byte[] Wtxid { get; set; } = [];
    public bool IsCoinbase { get; set; }

    /// <summary>The transaction as serialized in the block, witnesses included.</summary>
    public byte[] Raw { get; set; } = [];
}

/// <summary>One unspent coin: the UTXO set, row by row.</summary>
public sealed class CoinRecord
{
    public byte[] Txid { get; set; } = [];
    public int Vout { get; set; }
    public long Value { get; set; }
    public byte[] ScriptPubKey { get; set; } = [];
    public int Height { get; set; }
    public bool IsCoinbase { get; set; }
}

/// <summary>
/// Undo data, relational: one row per coin a block spent, keyed by the spending transaction's
/// position in the block and the input's index. Disconnecting a block rebuilds BlockUndo from these.
/// </summary>
public sealed class UndoCoinRecord
{
    public byte[] BlockHash { get; set; } = [];
    public int TxPosition { get; set; }
    public int InputIndex { get; set; }
    public byte[] Txid { get; set; } = [];
    public int Vout { get; set; }
    public long Value { get; set; }
    public byte[] ScriptPubKey { get; set; } = [];
    public int Height { get; set; }
    public bool IsCoinbase { get; set; }
}

/// <summary>An input whose script our code did not verify: accepted on Core's word, and why.</summary>
public sealed class UnverifiedScriptRecord
{
    public byte[] BlockHash { get; set; } = [];
    public byte[] Txid { get; set; } = [];
    public int InputIndex { get; set; }
    public string Reason { get; set; } = "";
}
