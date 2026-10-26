using MiniBtc.Domain.Blocks;
using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Transactions;
using MiniBtc.Domain.Validation;
using Patterns.DDD;

namespace MiniBtc.Domain.Utxo;

/// <summary>
/// The set of every spendable coin at the current tip (Core: the chainstate's CCoinsView).
/// It is the aggregate that guards the node's central invariant: a coin exists until exactly one
/// transaction in the active chain spends it. All changes go through <see cref="Connect"/>,
/// one whole block at a time, and each one raises <see cref="BlockConnected"/>.
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

    public Coin? GetCoin(OutPoint outPoint) => _coins.GetValueOrDefault(outPoint);

    /// <summary>
    /// Validates a block against the tip and, if it passes, applies it as Core's ConnectBlock does:
    /// spend each input, add each spendable output, collect the spent coins as undo data. All or
    /// nothing: on failure the set is unchanged. The header rules run first; transaction and block
    /// rules (values, maturity, subsidy, scripts) join them in the next commits.
    /// A block on another parent fails with prev-blk-not-found: choosing a branch is the caller's job.
    /// </summary>
    public Result<BlockUndo> Connect(Block block)
    {
        ArgumentNullException.ThrowIfNull(block);
        var context = new BlockContext(block, Tip, _network);

        var headerCheck = HeaderRules.All.Check(context);
        if (headerCheck.IsFailure)
            return headerCheck.Error;

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
        foreach (var tx in block.Transactions)
        {
            if (!tx.IsCoinbase)
            {
                var spent = new List<Coin>(tx.Inputs.Count);
                foreach (var input in tx.Inputs)
                {
                    var coin = view.Spend(input.PrevOut);
                    if (coin is null)
                        return new Error("bad-txns-inputs-missingorspent",
                            $"tx {tx.Txid} spends {input.PrevOut}, which is missing or already spent");
                    spent.Add(coin);
                }
                undo.Add(new TxUndo(spent));
            }

            for (var index = 0; index < tx.Outputs.Count; index++)
            {
                var output = tx.Outputs[index];
                if (!output.IsUnspendable)
                    view.Add(new OutPoint(tx.Txid, (uint)index), new Coin(output, height, tx.IsCoinbase));
            }
        }

        view.CommitTo(_coins);
        Tip = new ChainTip(block.Hash, height);
        Raise(new BlockConnected(block.Hash, height, block.Transactions.Count));
        return new BlockUndo(undo);
    }

    /// <summary>
    /// Pending changes on top of the set: coins this block created, and outpoints it spent from the
    /// set. A coin created earlier in the same block can be spent by a later transaction.
    /// </summary>
    private sealed class CoinsView(IReadOnlyDictionary<OutPoint, Coin> parent)
    {
        private readonly Dictionary<OutPoint, Coin> _added = new();
        private readonly HashSet<OutPoint> _spentFromParent = new();

        public Coin? Spend(OutPoint outPoint)
        {
            if (_added.Remove(outPoint, out var created))
                return created;
            if (!_spentFromParent.Contains(outPoint) && parent.TryGetValue(outPoint, out var existing))
            {
                _spentFromParent.Add(outPoint);
                return existing;
            }
            return null;
        }

        public void Add(OutPoint outPoint, Coin coin) => _added.Add(outPoint, coin);

        public void CommitTo(Dictionary<OutPoint, Coin> coins)
        {
            foreach (var outPoint in _spentFromParent) coins.Remove(outPoint);
            foreach (var (outPoint, coin) in _added) coins.Add(outPoint, coin);
        }
    }
}
