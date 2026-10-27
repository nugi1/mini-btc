using MiniBtc.Application.Abstractions;
using MiniBtc.Application.Explorer;
using MiniBtc.Application.Tests.ChainSync;
using MiniBtc.Application.Unconfirmed;
using MiniBtc.Domain.Blocks;
using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Tests.TestData;
using MiniBtc.Domain.Transactions;
using MiniBtc.Domain.Utxo;
using static MiniBtc.Domain.Tests.TestData.TestChain;

namespace MiniBtc.Application.Tests.Explorer;

public class ExplorerQueriesTests
{
    private const long Btc = ConsensusConstants.Coin;
    private const string KeyOneAddress = "mrCDrCybB6J1vRfbwM5hemdJz73FwDBC8r";
    private static readonly byte[] KeyOne = TestSigner.KeyOne.P2pkhScript;

    private readonly MempoolState _mempool = new(NetworkParams.Regtest);
    private readonly FakeChain _chain = new();
    private readonly Transaction _payment;
    private readonly Block _block;

    public ExplorerQueriesTests()
    {
        // Block 101 holds a signed payment of key one's 50 BTC block-1 coinbase: 10 to OP_TRUE, 39 back, 1 fee.
        var (utxos, coinbase) = ChainWithCoinbaseAtHeight1(tipHeight: 100, payTo: KeyOne);
        _payment = TestSigner.KeyOne.SignInput(new Transaction(2,
            [new TxIn(new OutPoint(coinbase.Txid, 0), ReadOnlySpan<byte>.Empty)],
            [new TxOut(10 * Btc, AnyoneCanSpend), new TxOut(39 * Btc, KeyOne)], 0), 0);
        _block = MinedBlock(utxos.Tip.Hash, Coinbase(101), _payment);
        var spentCoin = utxos.GetCoin(new OutPoint(coinbase.Txid, 0))!;
        _chain.Add(_block, 101, tipHeight: 105, spent: new Dictionary<(int, int), Coin> { [(1, 0)] = spentCoin }, unspent: [1]);
    }

    private ExplorerQueries Queries() => new(_chain, new NoStats(), new NoCoins(), _mempool, new FakeCore().Rpc, NetworkParams.Regtest);

    [Fact]
    public async Task A_confirmed_payment_is_decoded_with_the_coins_it_spent_and_which_outputs_are_spent()
    {
        var tx = (await Queries().GetTransactionAsync(_payment.Txid.ToString()))!;

        Assert.Equal(("confirmed", (int?)101, (int?)5), (tx.Status, tx.BlockHeight, tx.Confirmations));
        Assert.Equal((50 * Btc, 49 * Btc, (long?)1 * Btc), (tx.TotalIn, tx.TotalOut, tx.Fee));
        var input = Assert.Single(tx.Inputs);
        Assert.Equal((KeyOneAddress, "pubkeyhash", (long?)50 * Btc), (input.Address, input.Type, input.Value));
        Assert.Equal(2, input.ScriptSigPushes.Count); // <signature> <public key>
        Assert.Equal(TestSigner.KeyOne.PublicKey, Convert.FromHexString(input.ScriptSigPushes[1]));
        Assert.Equal(new bool?[] { true, false }, tx.Outputs.Select(o => o.Spent));
        Assert.Equal(KeyOneAddress, tx.Outputs[1].Address);
        Assert.Equal("nonstandard", tx.Outputs[0].Type); // OP_TRUE
        Assert.Equal(tx.Size, tx.Vsize); // no witness
    }

    [Fact]
    public async Task A_block_lists_its_transactions_with_fees_and_sizes()
    {
        var block = (await Queries().GetBlockAsync("101"))!;

        Assert.Equal(_block.Hash.ToString(), block.Hash);
        Assert.Equal(_block.Header.PreviousBlockHash.ToString(), block.PreviousHash);
        Assert.Equal(50 * Btc, block.Subsidy);
        Assert.Equal(_block.ToBytes().Length * 4, block.Weight); // no witnesses: weight is 4 × size
        Assert.Collection(block.Transactions,
            coinbase => Assert.Equal((true, (long?)null), (coinbase.Coinbase, coinbase.Fee)),
            payment => Assert.Equal((false, (long?)1 * Btc), (payment.Coinbase, payment.Fee)));
        Assert.Equal(101, (await Queries().GetBlockAsync(_block.Hash.ToString()))!.Height);
    }

    [Fact]
    public async Task Unknown_or_malformed_ids_find_nothing()
    {
        Assert.Null(await Queries().GetBlockAsync("not-a-block"));
        Assert.Null(await Queries().GetTransactionAsync(new string('a', 63)));
        Assert.Null(await Queries().GetTransactionAsync(new string('a', 64)));
    }

    [Fact]
    public async Task A_mempool_transaction_is_found_in_the_mempool_view()
    {
        var pending = TestSigner.KeyOne.SignInput(new Transaction(2,
            [new TxIn(new OutPoint(_payment.Txid, 1), ReadOnlySpan<byte>.Empty)], [new TxOut(38 * Btc, KeyOne)], 0), 0);
        _mempool.Update(view => view.Add(pending));

        var tx = (await Queries().GetTransactionAsync(pending.Txid.ToString()))!;

        Assert.Equal(("mempool", (int?)null), (tx.Status, tx.Confirmations));
        Assert.Null(tx.Fee); // NoCoins knows no UTXOs, so the input's value is unknown
    }

    private sealed class FakeChain : IExplorerReadModel
    {
        private readonly List<(Block Block, int Height, int TipHeight, Dictionary<(int, int), Coin> Spent, HashSet<int> Unspent)> _blocks = [];

        public void Add(Block block, int height, int tipHeight, Dictionary<(int, int), Coin> spent, HashSet<int> unspent) =>
            _blocks.Add((block, height, tipHeight, spent, unspent));

        public Task<IReadOnlyList<StoredBlockRow>> GetLatestBlocksAsync(int limit, int? belowHeight, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StoredBlockRow>>(_blocks.Select(Row).ToList());

        public Task<StoredBlockDetail?> GetBlockAsync(UInt256? hash, int? height, CancellationToken cancellationToken = default)
        {
            var found = _blocks.Where(b => b.Block.Hash == hash || b.Height == height).ToList();
            return Task.FromResult(found.Count == 0 ? null : new StoredBlockDetail(
                Row(found[0]), null, found[0].TipHeight,
                found[0].Block.Transactions.Select(tx => tx.ToBytes()).ToList(),
                found[0].Spent.ToDictionary(e => e.Key, e => e.Value)));
        }

        public Task<StoredTransaction?> GetTransactionAsync(UInt256 txid, CancellationToken cancellationToken = default)
        {
            foreach (var b in _blocks)
            {
                for (var position = 0; position < b.Block.Transactions.Count; position++)
                {
                    var tx = b.Block.Transactions[position];
                    if (tx.Txid != txid) continue;
                    var spent = Enumerable.Range(0, tx.IsCoinbase ? 0 : tx.Inputs.Count).Select(i => b.Spent[(position, i)]).ToList();
                    return Task.FromResult<StoredTransaction?>(new StoredTransaction(
                        tx.ToBytes(), b.Block.Hash, b.Height, position, b.TipHeight, spent, b.Unspent));
                }
            }
            return Task.FromResult<StoredTransaction?>(null);
        }

        private static StoredBlockRow Row((Block Block, int Height, int TipHeight, Dictionary<(int, int), Coin> Spent, HashSet<int> Unspent) b) =>
            new(b.Block.Hash, b.Height, b.Block.Header.ToBytes(), b.Block.Transactions.Count, b.Block.ToBytes().Length, 1 * Btc);
    }

    private sealed class NoStats : IUtxoStats
    {
        public Task<UtxoSummary> GetSummaryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new UtxoSummary(0, UInt256.Zero, 0, 0));
    }

    private sealed class NoCoins : ICoinLookup
    {
        public Task<CoinSnapshot> GetCoinsAsync(IReadOnlyCollection<OutPoint> outPoints, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CoinSnapshot(0, new Dictionary<OutPoint, Coin>()));
    }
}
