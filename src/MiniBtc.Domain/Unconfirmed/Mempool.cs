using MiniBtc.Domain.Blocks;
using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Scripts;
using MiniBtc.Domain.Transactions;
using MiniBtc.Domain.Utxo;
using MiniBtc.Domain.Validation;
using Patterns.DDD;

namespace MiniBtc.Domain.Unconfirmed;

/// <summary>
/// Our view of the transactions waiting for a block (Core: CTxMemPool). Its invariant: an outpoint is
/// spent by at most one transaction in it. That is what makes a double spend visible before any block
/// settles it: the second spend of a coin finds the first in the spent-outpoint index.
/// <para>
/// Two ways in. <see cref="Check"/> is the gateway's judgment on a transaction someone wants to
/// broadcast, our own validation, run before Core sees it. <see cref="Add"/> records a transaction
/// Core accepted, on Core's word, because Core is the network: whatever its mempool holds is what
/// miners will pick from.
/// </para>
/// </summary>
public sealed class Mempool : AggregateRoot<string>
{
    /// <summary>The height Core gives coins created by mempool transactions (txmempool.h MEMPOOL_HEIGHT).</summary>
    public const int MempoolHeight = 0x7FFFFFFF;

    private readonly Dictionary<UInt256, Transaction> _transactions = new();
    private readonly Dictionary<OutPoint, UInt256> _spentBy = new();

    public Mempool(NetworkParams network) : base(network.Name)
    {
    }

    public int Count => _transactions.Count;
    public IReadOnlyCollection<Transaction> Transactions => _transactions.Values;
    public bool Contains(UInt256 txid) => _transactions.ContainsKey(txid);
    public Transaction? Get(UInt256 txid) => _transactions.GetValueOrDefault(txid);

    /// <summary>The mempool transaction that spends <paramref name="outPoint"/>, if any.</summary>
    public UInt256? SpenderOf(OutPoint outPoint) => _spentBy.TryGetValue(outPoint, out var txid) ? txid : null;

    /// <summary>
    /// Would this transaction be valid in the next block, given the confirmed coins and the mempool?
    /// Core's order (validation.cpp PreChecks, then CheckInputScripts) and Core's reject reasons:
    /// CheckTransaction; "coinbase"; "txn-already-in-mempool"; a conflict; missing inputs (or the
    /// transaction is already confirmed);
    /// CheckTxInputs at the next block's height; then scripts. Changes nothing.
    /// </summary>
    /// <param name="confirmedCoin">Looks a coin up in the UTXO set at the tip.</param>
    /// <param name="spendHeight">Tip height + 1: the first block the transaction could be in.</param>
    public Result<CheckedTransaction> Check(Transaction tx, Func<OutPoint, Coin?> confirmedCoin, int spendHeight)
    {
        ArgumentNullException.ThrowIfNull(tx);
        ArgumentNullException.ThrowIfNull(confirmedCoin);

        var structure = TransactionRules.All.Check(tx);
        if (structure.IsFailure)
            return structure.Error;

        if (tx.IsCoinbase)
            return new Error(MempoolErrors.Coinbase, "a coinbase is only valid in a block");

        if (Contains(tx.Txid))
            return new Error(MempoolErrors.AlreadyInMempool, $"{tx.Txid} is already in the mempool");

        // Core v30 treats a conflict as a replacement attempt (full RBF) and judges its fees. This
        // node does not replace: Core's own reason for that case is bip125-replacement-disallowed.
        foreach (var input in tx.Inputs)
        {
            if (SpenderOf(input.PrevOut) is { } spender)
                return new Error(MempoolErrors.ReplacementDisallowed,
                    $"{input.PrevOut} is already spent by mempool transaction {spender}; this node does not replace transactions");
        }

        var coins = tx.Inputs.Select(input => confirmedCoin(input.PrevOut) ?? MempoolCoin(input.PrevOut)).ToArray();
        if (coins.Any(coin => coin is null))
        {
            // Inputs missing because the transaction itself is already confirmed? (Core checks the same.)
            for (var index = 0; index < tx.Outputs.Count; index++)
            {
                if (confirmedCoin(new OutPoint(tx.Txid, (uint)index)) is not null)
                    return new Error(MempoolErrors.AlreadyKnown, $"{tx.Txid} is already in a block");
            }
            return new Error(MempoolErrors.MissingInputs, "inputs missing or already spent in a block");
        }

        var spend = new TxSpendContext(tx, coins, spendHeight);
        var inputs = TxInputRules.All.Check(spend);
        if (inputs.IsFailure)
            return inputs.Error;

        var unverified = new List<int>();
        for (var i = 0; i < tx.Inputs.Count; i++)
        {
            var script = P2pkhVerifier.Verify(tx, i, coins[i]!.Output);
            if (script.Status == ScriptStatus.Failed)
                return new Error($"{MempoolErrors.ScriptFailedPrefix} ({script.Detail})", $"input {i} of tx {tx.Txid}");
            if (script.Status == ScriptStatus.NotVerified)
                unverified.Add(i);
        }

        return new CheckedTransaction(tx, TxInputRules.Fee(spend), unverified);
    }

    /// <summary>
    /// Records a transaction Core holds. Any transaction it conflicts with is evicted, with everything
    /// that spends from it: Core only accepts a conflict as a replacement, so the old one is gone there.
    /// Returns false if it was already here.
    /// </summary>
    public bool Add(Transaction tx)
    {
        ArgumentNullException.ThrowIfNull(tx);
        if (tx.IsCoinbase) throw new ArgumentException("A coinbase can't be in a mempool.", nameof(tx));
        if (Contains(tx.Txid)) return false;

        foreach (var input in tx.Inputs)
        {
            if (SpenderOf(input.PrevOut) is { } conflict)
                RemoveWithDescendants(conflict, removed: []);
        }

        _transactions.Add(tx.Txid, tx);
        foreach (var input in tx.Inputs) _spentBy[input.PrevOut] = tx.Txid;
        return true;
    }

    /// <summary>Removes one transaction (Core's ZMQ "R": evicted, expired or replaced). False if absent.</summary>
    public bool Remove(UInt256 txid)
    {
        if (!_transactions.Remove(txid, out var tx)) return false;
        foreach (var input in tx.Inputs)
        {
            if (_spentBy.TryGetValue(input.PrevOut, out var spender) && spender == txid)
                _spentBy.Remove(input.PrevOut);
        }
        return true;
    }

    /// <summary>
    /// A block was connected (Core: removeForBlock). Its transactions are confirmed now, and any mempool
    /// transaction spending a coin the block spent can never confirm, nor can its descendants.
    /// Returns every txid removed.
    /// </summary>
    public IReadOnlyList<UInt256> RemoveForBlock(Block block)
    {
        ArgumentNullException.ThrowIfNull(block);
        var removed = new List<UInt256>();
        foreach (var tx in block.Transactions)
        {
            if (Remove(tx.Txid)) removed.Add(tx.Txid);
        }
        foreach (var tx in block.Transactions.Where(tx => !tx.IsCoinbase))
        {
            foreach (var input in tx.Inputs)
            {
                if (SpenderOf(input.PrevOut) is { } conflict)
                    RemoveWithDescendants(conflict, removed);
            }
        }
        return removed;
    }

    /// <summary>
    /// A block was disconnected: its transactions are unconfirmed again and go back to the mempool view,
    /// parents before children as in the block. Core re-validates them the same way and announces
    /// each over ZMQ, which <see cref="Add"/> then finds already present.
    /// </summary>
    public void ReturnFromDisconnectedBlock(Block block)
    {
        ArgumentNullException.ThrowIfNull(block);
        foreach (var tx in block.Transactions.Where(tx => !tx.IsCoinbase)) Add(tx);
    }

    private Coin? MempoolCoin(OutPoint outPoint) =>
        _transactions.TryGetValue(outPoint.Txid, out var parent) && outPoint.Index < parent.Outputs.Count
            ? new Coin(parent.Outputs[(int)outPoint.Index], MempoolHeight, IsCoinbase: false)
            : null;

    private void RemoveWithDescendants(UInt256 txid, List<UInt256> removed)
    {
        if (!_transactions.TryGetValue(txid, out var tx)) return;
        Remove(txid);
        removed.Add(txid);
        for (var index = 0; index < tx.Outputs.Count; index++)
        {
            if (SpenderOf(new OutPoint(txid, (uint)index)) is { } child)
                RemoveWithDescendants(child, removed);
        }
    }
}

/// <summary>A transaction that passed <see cref="Mempool.Check"/>.</summary>
/// <param name="Fee">Inputs minus outputs, in satoshis.</param>
/// <param name="UnverifiedInputs">Inputs whose script is not P2PKH: left to Core.</param>
public sealed record CheckedTransaction(Transaction Transaction, long Fee, IReadOnlyList<int> UnverifiedInputs);

/// <summary>Bitcoin Core v30.0's reject reasons for the cases the mempool decides (validation.cpp).</summary>
public static class MempoolErrors
{
    public const string Coinbase = "coinbase";
    public const string AlreadyInMempool = "txn-already-in-mempool";
    public const string ReplacementDisallowed = "bip125-replacement-disallowed";
    public const string AlreadyKnown = "txn-already-known";
    public const string MissingInputs = "bad-txns-inputs-missingorspent";
    public const string ScriptFailedPrefix = "mempool-script-verify-flag-failed";
}
