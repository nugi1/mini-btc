using MiniBtc.Domain.Blocks;
using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Transactions;
using MiniBtc.Domain.Utxo;
using MiniBtc.Infrastructure.Persistence;
using static MiniBtc.Domain.Tests.TestData.TestChain;

namespace MiniBtc.Integration.Tests.Persistence;

public sealed class ExplorerReadModelTests : IAsyncLifetime
{
    private const long Btc = ConsensusConstants.Coin;
    private readonly TestDatabase _database = new();
    private Transaction _payment = null!;
    private Block _paymentBlock = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        await using var db = _database.CreateContext();
        var store = new ChainStore(db);
        var utxos = await store.LoadAsync(NetworkParams.Regtest);
        await db.SaveChangesAsync();
        var coinbase = Coinbase(1);
        await ConnectAndSave(db, store, utxos, MinedBlock(Genesis, coinbase));
        for (var height = 2; height <= 100; height++)
            await ConnectAndSave(db, store, utxos, MinedBlock(utxos.Tip.Hash, Coinbase(height)));

        _payment = Spend([new OutPoint(coinbase.Txid, 0)], 10 * Btc, 39 * Btc);
        _paymentBlock = MinedBlock(utxos.Tip.Hash, Coinbase(101), _payment);
        await ConnectAndSave(db, store, utxos, _paymentBlock);
        await ConnectAndSave(db, store, utxos, MinedBlock(utxos.Tip.Hash, Coinbase(102),
            Spend([new OutPoint(_payment.Txid, 0)], 9 * Btc)));
    }

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task A_stored_transaction_comes_with_the_coins_it_spent_and_its_unspent_outputs()
    {
        await using var db = _database.CreateContext();

        var stored = (await new ExplorerReadModel(db, NetworkParams.Regtest).GetTransactionAsync(_payment.Txid))!;

        Assert.Equal(_payment.ToBytes(), stored.Raw);
        Assert.Equal((101, 1, 102), (stored.BlockHeight, stored.Position, stored.TipHeight));
        var spent = Assert.Single(stored.SpentCoins);
        Assert.Equal((50 * Btc, 1, true), (spent.Value, spent.Height, spent.IsCoinbase));
        Assert.Equal(new[] { 1 }, stored.UnspentOutputs); // output 0 was spent in block 102
    }

    [Fact]
    public async Task A_block_by_height_or_hash_has_its_transactions_undo_coins_and_next_block()
    {
        await using var db = _database.CreateContext();
        var model = new ExplorerReadModel(db, NetworkParams.Regtest);

        var byHeight = (await model.GetBlockAsync(null, 101))!;
        var byHash = (await model.GetBlockAsync(_paymentBlock.Hash, null))!;

        Assert.Equal(_paymentBlock.Hash, byHeight.Block.Hash);
        Assert.Equal(byHeight.Block.Hash, byHash.Block.Hash);
        Assert.Equal(_paymentBlock.Transactions.Select(tx => tx.ToBytes()), byHeight.Transactions);
        Assert.Equal(50 * Btc, byHeight.SpentCoins[(1, 0)].Value);
        Assert.NotNull(byHeight.NextHash);
        Assert.Null(await model.GetBlockAsync(null, 103));
    }

    [Fact]
    public async Task Latest_blocks_come_newest_first_and_page_by_height()
    {
        await using var db = _database.CreateContext();
        var model = new ExplorerReadModel(db, NetworkParams.Regtest);

        var first = await model.GetLatestBlocksAsync(3, null);
        var next = await model.GetLatestBlocksAsync(3, first[^1].Height);

        Assert.Equal(new[] { 102, 101, 100 }, first.Select(b => b.Height));
        Assert.Equal(new[] { 99, 98, 97 }, next.Select(b => b.Height));
    }

    [Fact]
    public async Task Utxo_stats_count_and_sum_the_stored_set_at_the_stored_tip()
    {
        await using var db = _database.CreateContext();

        var summary = await new UtxoStats(db, NetworkParams.Regtest).GetSummaryAsync();

        // 102 coinbases, minus block 1's (spent), plus the payment's change and block 102's 9 BTC output.
        Assert.Equal((102, 103L), (summary.TipHeight, summary.CoinCount));
        Assert.Equal(101 * 50 * Btc + 39 * Btc + 9 * Btc, summary.TotalAmount);
    }

    private static async Task ConnectAndSave(MiniBtcDbContext db, ChainStore store, UtxoSet utxos, Block block)
    {
        var result = utxos.Connect(block);
        Assert.True(result.IsSuccess, result.Error?.ToString());
        store.AddConnectedBlock(block, result.Value);
        await db.SaveChangesAsync();
    }
}
