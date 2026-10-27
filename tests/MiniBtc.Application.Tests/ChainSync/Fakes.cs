using MiniBtc.Application.Abstractions;
using MiniBtc.Domain.Blocks;
using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Transactions;
using MiniBtc.Domain.Utxo;
using Patterns.DDD.Application;
using static MiniBtc.Domain.Tests.TestData.TestChain;

namespace MiniBtc.Application.Tests.ChainSync;

/// <summary>A Bitcoin Core stand-in: a chain of blocks served over the IBitcoinCoreRpc port.</summary>
internal sealed class FakeCore
{
    private readonly List<Block> _chain = [NetworkParams.Regtest.GenesisBlock];

    public string Chain { get; set; } = "regtest";
    public int WarmupCallsLeft { get; set; }
    public UInt256? GenesisOverride { get; set; }
    public Func<int, byte[]?>? RawOverride { get; set; }
    public Action? OnBlockchainInfo { get; set; }
    public int RawBlockRequests { get; private set; }

    public IReadOnlyList<Block> Blocks => _chain;
    public int Height => _chain.Count - 1;

    /// <summary>Core's mempool: what sendrawtransaction accepted, in order.</summary>
    public List<Transaction> Mempool { get; } = [];
    public ulong MempoolSequence { get; set; }

    /// <summary>When set, sendrawtransaction fails with this RPC error instead.</summary>
    public (int Code, string Message)? RejectSends { get; set; }
    public int SendRequests { get; private set; }

    /// <summary>Like invalidateblock on the tip, <paramref name="count"/> times: the chain gets shorter.</summary>
    public FakeCore Invalidate(int count)
    {
        _chain.RemoveRange(_chain.Count - count, count);
        return this;
    }

    /// <summary>Mines a branch whose coinbases pay 49 BTC, so its blocks differ from any mined by <see cref="Mine"/>.</summary>
    public FakeCore MineFork(int count)
    {
        for (var i = 0; i < count; i++)
            Append(MinedBlock(_chain[^1].Hash, Coinbase(_chain.Count, 49 * ConsensusConstants.Coin)));
        return this;
    }

    /// <summary>Mines <paramref name="count"/> coinbase-only blocks on top of the chain.</summary>
    public FakeCore Mine(int count)
    {
        for (var i = 0; i < count; i++) Append(MinedBlock(_chain[^1].Hash, Coinbase(_chain.Count)));
        return this;
    }

    public FakeCore Append(Block block)
    {
        _chain.Add(block);
        return this;
    }

    public IBitcoinCoreRpc Rpc => new Port(this);

    private sealed class Port(FakeCore core) : IBitcoinCoreRpc
    {
        public Task<BlockchainInfo> GetBlockchainInfoAsync(CancellationToken cancellationToken = default)
        {
            if (core.WarmupCallsLeft > 0)
            {
                core.WarmupCallsLeft--;
                throw new BitcoinCoreRpcException("getblockchaininfo", RpcErrorCodes.InWarmup, "Loading block index…");
            }
            var info = new BlockchainInfo(core.Chain, core.Height, core.Height, core._chain[^1].Hash);
            core.OnBlockchainInfo?.Invoke();
            return Task.FromResult(info);
        }

        public Task<UInt256> GetBlockHashAsync(int height, CancellationToken cancellationToken = default) =>
            Task.FromResult(height == 0 && core.GenesisOverride is { } genesis ? genesis : core._chain[height].Hash);

        public Task<byte[]> GetRawBlockAsync(UInt256 blockHash, CancellationToken cancellationToken = default)
        {
            core.RawBlockRequests++;
            var height = core._chain.FindIndex(b => b.Hash == blockHash);
            return Task.FromResult(core.RawOverride?.Invoke(height) ?? core._chain[height].ToBytes());
        }

        public Task<UInt256> SendRawTransactionAsync(string hex, CancellationToken cancellationToken = default)
        {
            core.SendRequests++;
            if (core.RejectSends is { } reject)
                throw new BitcoinCoreRpcException("sendrawtransaction", reject.Code, reject.Message);
            var tx = Transaction.Parse(Convert.FromHexString(hex));
            core.Mempool.Add(tx);
            return Task.FromResult(tx.Txid);
        }

        public Task<byte[]> GetRawTransactionAsync(UInt256 txid, CancellationToken cancellationToken = default) =>
            core.Mempool.FirstOrDefault(tx => tx.Txid == txid) is { } tx
                ? Task.FromResult(tx.ToBytes())
                : throw new BitcoinCoreRpcException("getrawtransaction", RpcErrorCodes.InvalidAddressOrKey,
                    "No such mempool or blockchain transaction. Use gettransaction for wallet transactions.");

        public Task<RawMempool> GetRawMempoolAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new RawMempool(core.Mempool.Select(tx => tx.Txid).ToList(), core.MempoolSequence));
    }
}

/// <summary>
/// An in-memory database behind IChainStore and IUnitOfWork: staged blocks become durable only when
/// SaveChangesAsync runs, and LoadAsync rebuilds the UTXO set from what is durable, like the real store.
/// </summary>
internal sealed class FakeDatabase
{
    private readonly List<Block> _committed = [];
    private readonly Dictionary<UInt256, BlockUndo> _undo = new();
    private readonly List<Block> _staged = [];
    private readonly List<UInt256> _stagedRemovals = [];
    private bool _genesisStaged;
    private UtxoSet? _loaded;

    public IReadOnlyList<Block> Committed => _committed;
    public bool GenesisCommitted { get; private set; }
    public int Saves { get; private set; }

    public IChainStore Store => new StorePort(this);
    public IUnitOfWork UnitOfWork => new UnitOfWorkPort(this);

    /// <summary>Puts blocks in the database as if an earlier run had stored them.</summary>
    public void Seed(IEnumerable<Block> blocks)
    {
        GenesisCommitted = true;
        _committed.AddRange(blocks);
    }

    private sealed class StorePort(FakeDatabase db) : IChainStore
    {
        public Task<UtxoSet> LoadAsync(NetworkParams network, CancellationToken cancellationToken = default)
        {
            var utxos = UtxoSet.AtGenesis(network);
            foreach (var block in db._committed)
            {
                var connected = utxos.Connect(block);
                if (connected.IsFailure) throw new InvalidOperationException("seeded chain is invalid");
                db._undo[block.Hash] = connected.Value.Undo;
            }
            if (!db.GenesisCommitted) db._genesisStaged = true;
            db._loaded = utxos;
            return Task.FromResult(utxos);
        }

        public void AddConnectedBlock(Block block, ConnectedBlock connected)
        {
            db._staged.Add(block);
            db._undo[block.Hash] = connected.Undo;
        }

        public Task<StoredBlock> LoadTipBlockAsync(CancellationToken cancellationToken = default)
        {
            var tip = db._committed.Single(b => b.Hash == db._loaded!.Tip.Hash);
            return Task.FromResult(new StoredBlock(tip, db._undo[tip.Hash]));
        }

        public void AddDisconnectedBlock(Block block, DisconnectedBlock disconnected) => db._stagedRemovals.Add(block.Hash);
    }

    private sealed class UnitOfWorkPort(FakeDatabase db) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            db.Saves++;
            var rows = db._staged.Count + db._stagedRemovals.Count + (db._genesisStaged ? 1 : 0);
            db._committed.RemoveAll(block => db._stagedRemovals.Contains(block.Hash));
            db._stagedRemovals.Clear();
            db._committed.AddRange(db._staged);
            db._staged.Clear();
            if (db._genesisStaged) (db.GenesisCommitted, db._genesisStaged) = (true, false);
            return Task.FromResult(rows);
        }
    }
}
