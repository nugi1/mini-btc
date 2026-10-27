using MiniBtc.Domain.Blocks;
using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Scripts;
using MiniBtc.Domain.Transactions;
using MiniBtc.Domain.Validation;
using Patterns.DDD;

namespace MiniBtc.Domain.Utxo;

/// <summary>
/// The set of every spendable coin at the current tip (Core: the chainstate's CCoinsView).
/// It is the aggregate that guards the node's central invariant: a coin exists until exactly one
/// transaction in the active chain spends it. All changes go through <see cref="Connect"/>,
/// or <see cref="Disconnect"/>, one whole block at a time, and each raises <see cref="BlockConnected"/>
/// or <see cref="BlockDisconnected"/>.
/// Identity: the network it follows; the node keeps one UTXO set per chain.
/// </summary>
public sealed class UtxoSet : AggregateRoot<string>
{
    private readonly NetworkParams _network;
    private readonly Dictionary<OutPoint, Coin> _coins;

    private UtxoSet(NetworkParams network, ChainTip tip, Dictionary<OutPoint, Coin> coins) : base(network.Name)
    {
        _network = network;
        Tip = tip;
        _coins = coins;
    }

    public ChainTip Tip { get; private set; }
    public IReadOnlyDictionary<OutPoint, Coin> Coins => _coins;

    /// <summary>
    /// An empty set whose tip is the genesis block. Genesis adds no coin: Core skips connecting its
    /// transactions, so the first 50 BTC can never be spent (validation.cpp ConnectBlock).
    /// </summary>
    public static UtxoSet AtGenesis(NetworkParams network) =>
        new(network, new ChainTip(network.GenesisHash, 0), new Dictionary<OutPoint, Coin>());

    /// <summary>
    /// Rehydrates a set from storage: the tip it reflected and the coins it held. Persistence calls
    /// this; it is the only way back into the aggregate besides replaying every block.
    /// </summary>
    public static UtxoSet Restore(NetworkParams network, ChainTip tip, IEnumerable<KeyValuePair<OutPoint, Coin>> coins) =>
        new(network, tip, new Dictionary<OutPoint, Coin>(coins));

    public Coin? GetCoin(OutPoint outPoint) => _coins.GetValueOrDefault(outPoint);

    /// <summary>
    /// Validates a block against the tip and, if it passes, applies it as Core's ConnectBlock does:
    /// spend each input, add each spendable output, collect the spent coins as undo data. All or
    /// nothing: on failure the set is unchanged. Rules run where Core runs them. CheckBlock: header
    /// rules, block structure, CheckTransaction on every tx. ConnectBlock: BIP30, CheckTxInputs per
    /// tx as it is applied (summing fees) and its scripts, and finally the coinbase amount.
    /// A block on another parent fails with prev-blk-not-found: choosing a branch is the caller's job.
    /// </summary>
    public Result<ConnectedBlock> Connect(Block block)
    {
        ArgumentNullException.ThrowIfNull(block);
        var context = new BlockContext(block, Tip, _network);

        var headerCheck = HeaderRules.All.Check(context);
        if (headerCheck.IsFailure)
            return headerCheck.Error;

        var structureCheck = BlockRules.Structure.Check(context);
        if (structureCheck.IsFailure)
            return structureCheck.Error;

        var transactionsCheck = TransactionRules.EachInBlock.Check(context);
        if (transactionsCheck.IsFailure)
            return transactionsCheck.Error;

        var height = context.Height;

        // BIP30: no transaction may create an outpoint that is still unspent. Checked against the
        // set as it stood before this block, like Core.
        foreach (var tx in block.Transactions)
        {
            for (var index = 0; index < tx.Outputs.Count; index++)
            {
                if (_coins.ContainsKey(new OutPoint(tx.Txid, (uint)index)))
                    return new Error("bad-txns-BIP30", $"tried to overwrite transaction {tx.Txid}");
            }
        }

        // Work on a view over the set (Core: CCoinsViewCache) and commit only if the whole block applies.
        var view = new CoinsView(_coins);
        var undo = new List<TxUndo>();
        var unverified = new List<UnverifiedScript>();
        long fees = 0;
        foreach (var tx in block.Transactions)
        {
            if (!tx.IsCoinbase)
            {
                // Resolve each input against the view (which includes this block's earlier txs), check, then spend.
                var coins = tx.Inputs.Select(input => view.Get(input.PrevOut)).ToArray();
                var spend = new TxSpendContext(tx, coins, height);
                var inputsCheck = TxInputRules.All.Check(spend);
                if (inputsCheck.IsFailure)
                    return inputsCheck.Error;

                fees += TxInputRules.Fee(spend);
                if (!ConsensusConstants.IsMoneyRange(fees))
                    return new Error("bad-txns-accumulated-fee-outofrange", "accumulated fee in the block out of range");

                // Core: CheckInputScripts. Any failing input makes the whole block invalid.
                for (var i = 0; i < tx.Inputs.Count; i++)
                {
                    var script = P2pkhVerifier.Verify(tx, i, coins[i]!.Output);
                    if (script.Status == ScriptStatus.Failed)
                        return new Error($"block-script-verify-flag-failed ({script.Detail})", $"input {i} of tx {tx.Txid}");
                    if (script.Status == ScriptStatus.NotVerified)
                        unverified.Add(new UnverifiedScript(tx.Txid, i, script.Detail!));
                }

                foreach (var input in tx.Inputs) view.Spend(input.PrevOut);
                undo.Add(new TxUndo(coins.Select(coin => coin!)));
            }

            for (var index = 0; index < tx.Outputs.Count; index++)
            {
                var output = tx.Outputs[index];
                if (!output.IsUnspendable)
                    view.Add(new OutPoint(tx.Txid, (uint)index), new Coin(output, height, tx.IsCoinbase));
            }
        }

        var coinbaseCheck = BlockRules.CoinbaseAmount.Check(new CoinbaseContext(block, height, fees, _network));
        if (coinbaseCheck.IsFailure)
            return coinbaseCheck.Error;

        var changes = view.Changes();
        view.CommitTo(_coins);
        Tip = new ChainTip(block.Hash, height);
        Raise(new BlockConnected(block.Hash, height, block.Transactions.Count));
        return new ConnectedBlock(new BlockUndo(undo), changes, fees, unverified);
    }

    /// <summary>
    /// Undoes the tip block, as Core's DisconnectBlock does: walking the transactions backwards, it
    /// removes each one's outputs and puts back the coins its inputs spent, taken from the undo data
    /// stored when the block was connected. The tip moves to the block's parent. Every removed output
    /// must be exactly the coin the block created and every restored coin must be absent; Core calls
    /// a mismatch an unclean disconnect. Here it fails, and the set is left unchanged.
    /// </summary>
    public Result<DisconnectedBlock> Disconnect(Block block, BlockUndo undo)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(undo);
        if (block.Hash != Tip.Hash)
            return new Error(DisconnectErrors.NotTip, $"block {block.Hash} is not the tip {Tip.Hash}");
        if (Tip.Height == 0)
            return new Error(DisconnectErrors.NotTip, "the genesis block can't be disconnected");
        if (undo.Transactions.Count + 1 != block.Transactions.Count)
            return new Error(DisconnectErrors.BadUndo, "block and undo data inconsistent");

        var height = Tip.Height;
        var view = new CoinsView(_coins);
        for (var position = block.Transactions.Count - 1; position >= 0; position--)
        {
            var tx = block.Transactions[position];
            for (var index = 0; index < tx.Outputs.Count; index++)
            {
                var output = tx.Outputs[index];
                if (output.IsUnspendable) continue;
                var outPoint = new OutPoint(tx.Txid, (uint)index);
                var coin = view.Get(outPoint);
                if (coin is null || !coin.Output.Equals(output) || coin.Height != height || coin.IsCoinbase != tx.IsCoinbase)
                    return new Error(DisconnectErrors.Unclean, $"output {outPoint} is not the coin block {block.Hash} created");
                view.Spend(outPoint);
            }

            if (tx.IsCoinbase) continue;
            var spent = undo.Transactions[position - 1].SpentCoins;
            if (spent.Count != tx.Inputs.Count)
                return new Error(DisconnectErrors.BadUndo, $"undo data for tx {tx.Txid} has {spent.Count} coins for {tx.Inputs.Count} inputs");
            for (var input = tx.Inputs.Count - 1; input >= 0; input--)
            {
                var prevOut = tx.Inputs[input].PrevOut;
                if (view.Get(prevOut) is not null)
                    return new Error(DisconnectErrors.Unclean, $"restoring {prevOut} would overwrite an unspent coin");
                view.Restore(prevOut, spent[input]);
            }
        }

        var changes = view.Changes();
        view.CommitTo(_coins);
        Tip = new ChainTip(block.Header.PreviousBlockHash, height - 1);
        Raise(new BlockDisconnected(block.Hash, height, block.Transactions.Count));
        return new DisconnectedBlock(changes, block.Transactions.Where(tx => !tx.IsCoinbase).ToList());
    }

    /// <summary>
    /// Pending changes on top of the set: coins this block created, and outpoints it spent from the
    /// set. A coin created earlier in the same block can be spent by a later transaction.
    /// </summary>
    private sealed class CoinsView(IReadOnlyDictionary<OutPoint, Coin> parent)
    {
        private readonly Dictionary<OutPoint, Coin> _added = new();
        private readonly HashSet<OutPoint> _spentFromParent = new();

        public Coin? Get(OutPoint outPoint)
        {
            if (_added.TryGetValue(outPoint, out var created))
                return created;
            return !_spentFromParent.Contains(outPoint) && parent.TryGetValue(outPoint, out var existing) ? existing : null;
        }

        /// <summary>Marks a coin spent. Callers resolve it with <see cref="Get"/> and check it first.</summary>
        public void Spend(OutPoint outPoint)
        {
            if (!_added.Remove(outPoint))
                _spentFromParent.Add(outPoint);
        }

        public void Add(OutPoint outPoint, Coin coin) => _added.Add(outPoint, coin);

        /// <summary>Puts a spent coin back. Callers check with <see cref="Get"/> that the outpoint is free.</summary>
        public void Restore(OutPoint outPoint, Coin coin)
        {
            if (_spentFromParent.Remove(outPoint) && parent.TryGetValue(outPoint, out var original) && original == coin)
                return; // removed and put back unchanged: no net change
            _added[outPoint] = coin;
        }

        public UtxoChanges Changes() => new([.. _spentFromParent], [.. _added]);

        public void CommitTo(Dictionary<OutPoint, Coin> coins)
        {
            foreach (var outPoint in _spentFromParent) coins.Remove(outPoint);
            foreach (var (outPoint, coin) in _added) coins.Add(outPoint, coin);
        }
    }
}
