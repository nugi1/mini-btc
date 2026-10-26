using Microsoft.EntityFrameworkCore;
using MiniBtc.Domain.Blocks;
using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Transactions;
using MiniBtc.Domain.Utxo;
using MiniBtc.Infrastructure.Persistence;
using static MiniBtc.Domain.Tests.TestData.TestChain;

namespace MiniBtc.Integration.Tests.Persistence;

public sealed class ChainStoreTests : IAsyncLifetime
{
    private const long Btc = ConsensusConstants.Coin;
    private readonly TestDatabase _database = new();

    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task An_empty_database_starts_at_genesis_and_records_it()
    {
        await using (var db = _database.CreateContext())
        {
            var utxos = await new ChainStore(db).LoadAsync(NetworkParams.Regtest);
            Assert.Equal(new ChainTip(Genesis, 0), utxos.Tip);
            await db.SaveChangesAsync();
        }

        await using var fresh = _database.CreateContext();
        var genesis = Assert.Single(await fresh.Blocks.ToListAsync());
        Assert.Equal(Genesis, new UInt256(genesis.Hash));
        Assert.Equal(0, genesis.Height);
        Assert.Equal(285, genesis.Size);
        Assert.Empty(await fresh.Coins.ToListAsync()); // the genesis coinbase is unspendable
        Assert.Equal(0, (await fresh.ChainStates.SingleAsync()).TipHeight);
    }

    [Fact]
    public async Task A_chain_survives_a_restart_with_the_same_tip_and_coins()
    {
        UtxoSet before;
        Transaction payment;
        await using (var db = _database.CreateContext())
        {
            var store = new ChainStore(db);
            before = await store.LoadAsync(NetworkParams.Regtest);
            var coinbase = Coinbase(1);
            await ConnectAndSave(db, store, before, MinedBlock(Genesis, coinbase));
            for (var height = 2; height <= 100; height++)
                await ConnectAndSave(db, store, before, MinedBlock(before.Tip.Hash, Coinbase(height)));

            payment = Spend([new OutPoint(coinbase.Txid, 0)], 10 * Btc, 39 * Btc);
            await ConnectAndSave(db, store, before, MinedBlock(before.Tip.Hash, Coinbase(101), payment));
        }

        // A new context and a new load: what a restarted node would see.
        await using var restarted = _database.CreateContext();
        var after = await new ChainStore(restarted).LoadAsync(NetworkParams.Regtest);

        Assert.Equal(before.Tip, after.Tip);
        Assert.Equal(before.Coins.Count, after.Coins.Count);
        Assert.All(before.Coins, entry => Assert.Equal(entry.Value, after.GetCoin(entry.Key)));

        Assert.Equal(102, await restarted.Blocks.CountAsync());            // genesis + 101
        Assert.Equal(103, await restarted.Transactions.CountAsync());      // 102 coinbases + the payment
        var paymentBlock = await restarted.Blocks.SingleAsync(b => b.Height == 101);
        Assert.Equal(1 * Btc, paymentBlock.Fees);

        var undo = Assert.Single(await restarted.UndoCoins.ToListAsync());
        Assert.Equal((1, 0, 50 * Btc, 1, true), (undo.TxPosition, undo.InputIndex, undo.Value, undo.Height, undo.IsCoinbase));

        var flagged = Assert.Single(await restarted.UnverifiedScripts.ToListAsync()); // OP_TRUE: not P2PKH
        Assert.Equal(payment.Txid, new UInt256(flagged.Txid));
    }

    [Fact]
    public async Task A_coin_created_and_spent_in_one_block_never_reaches_the_database()
    {
        await using var db = _database.CreateContext();
        var store = new ChainStore(db);
        var utxos = await store.LoadAsync(NetworkParams.Regtest);
        var coinbase = Coinbase(1);
        await ConnectAndSave(db, store, utxos, MinedBlock(Genesis, coinbase));
        for (var height = 2; height <= 100; height++)
            await ConnectAndSave(db, store, utxos, MinedBlock(utxos.Tip.Hash, Coinbase(height)));

        var parent = Spend([new OutPoint(coinbase.Txid, 0)], 49 * Btc);
        var child = Spend([new OutPoint(parent.Txid, 0)], 48 * Btc);
        await ConnectAndSave(db, store, utxos, MinedBlock(utxos.Tip.Hash, Coinbase(101), parent, child));

        var parentTxid = parent.Txid.ToWireBytes();
        var childTxid = child.Txid.ToWireBytes();
        Assert.False(await db.Coins.AsNoTracking().AnyAsync(c => c.Txid == parentTxid));
        Assert.True(await db.Coins.AsNoTracking().AnyAsync(c => c.Txid == childTxid));
        Assert.Equal(utxos.Coins.Count, await db.Coins.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task A_rejected_block_stages_nothing()
    {
        await using var db = _database.CreateContext();
        var store = new ChainStore(db);
        var utxos = await store.LoadAsync(NetworkParams.Regtest);
        await db.SaveChangesAsync();

        var result = utxos.Connect(MinedBlock(Genesis, Coinbase(1, 51 * Btc))); // bad-cb-amount

        Assert.True(result.IsFailure);
        Assert.False(db.ChangeTracker.HasChanges());
    }

    private static async Task ConnectAndSave(MiniBtcDbContext db, ChainStore store, UtxoSet utxos, Block block)
    {
        var result = utxos.Connect(block);
        Assert.True(result.IsSuccess, result.Error?.ToString());
        store.AddConnectedBlock(block, result.Value);
        await db.SaveChangesAsync();
    }
}
