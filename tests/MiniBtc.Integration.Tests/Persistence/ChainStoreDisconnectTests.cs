using Microsoft.EntityFrameworkCore;
using MiniBtc.Domain.Blocks;
using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Transactions;
using MiniBtc.Domain.Utxo;
using MiniBtc.Infrastructure.Persistence;
using static MiniBtc.Domain.Tests.TestData.TestChain;

namespace MiniBtc.Integration.Tests.Persistence;

public sealed class ChainStoreDisconnectTests : IAsyncLifetime
{
    private const long Btc = ConsensusConstants.Coin;
    private readonly TestDatabase _database = new();

    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task A_disconnected_block_leaves_the_database_as_it_was_before_the_block()
    {
        Block paymentBlock;
        await using (var db = _database.CreateContext())
        {
            var store = new ChainStore(db);
            var utxos = await store.LoadAsync(NetworkParams.Regtest);
            await db.SaveChangesAsync();
            var coinbase = Coinbase(1);
            await ConnectAndSave(db, store, utxos, MinedBlock(Genesis, coinbase));
            for (var height = 2; height <= 100; height++)
                await ConnectAndSave(db, store, utxos, MinedBlock(utxos.Tip.Hash, Coinbase(height)));

            var parent = Spend([new OutPoint(coinbase.Txid, 0)], 10 * Btc, 39 * Btc);
            var child = Spend([new OutPoint(parent.Txid, 0)], 9 * Btc);
            paymentBlock = MinedBlock(utxos.Tip.Hash, Coinbase(101), parent, child);
            await ConnectAndSave(db, store, utxos, paymentBlock);
        }

        // A fresh context, as the next sync run would have, undoes the stored tip.
        await using (var db = _database.CreateContext())
        {
            var store = new ChainStore(db);
            var utxos = await store.LoadAsync(NetworkParams.Regtest);
            var stored = await store.LoadTipBlockAsync();
            Assert.Equal(paymentBlock.ToBytes(), stored.Block.ToBytes());

            var disconnected = utxos.Disconnect(stored.Block, stored.Undo);
            Assert.True(disconnected.IsSuccess, disconnected.Error?.ToString());
            store.AddDisconnectedBlock(stored.Block, disconnected.Value);
            await db.SaveChangesAsync();
        }

        await using var check = _database.CreateContext();
        var reloaded = await new ChainStore(check).LoadAsync(NetworkParams.Regtest);
        Assert.Equal(100, reloaded.Tip.Height);
        Assert.Equal(paymentBlock.Header.PreviousBlockHash, reloaded.Tip.Hash);
        Assert.Equal(100, reloaded.Coins.Count); // the 100 coinbases, block 1's restored
        Assert.Equal(100, await check.Coins.CountAsync());
        Assert.Equal(101, await check.Blocks.CountAsync()); // genesis + 100
        Assert.Empty(await check.UndoCoins.ToListAsync());
        Assert.Empty(await check.UnverifiedScripts.ToListAsync());
        Assert.Equal(101, await check.Transactions.CountAsync());
        Assert.Equal(100, (await check.ChainStates.SingleAsync()).TipHeight);
    }

    [Fact]
    public async Task One_context_can_disconnect_and_then_connect_a_replacement_at_the_same_height()
    {
        await using var db = _database.CreateContext();
        var store = new ChainStore(db);
        var utxos = await store.LoadAsync(NetworkParams.Regtest);
        await db.SaveChangesAsync();
        await ConnectAndSave(db, store, utxos, MinedBlock(Genesis, Coinbase(1)));
        var replaced = MinedBlock(utxos.Tip.Hash, Coinbase(2));
        await ConnectAndSave(db, store, utxos, replaced);

        var stored = await store.LoadTipBlockAsync();
        store.AddDisconnectedBlock(stored.Block, utxos.Disconnect(stored.Block, stored.Undo).Value);
        await db.SaveChangesAsync();
        var replacement = MinedBlock(utxos.Tip.Hash, Coinbase(2, 49 * Btc));
        await ConnectAndSave(db, store, utxos, replacement);

        var tip = await db.Blocks.AsNoTracking().SingleAsync(b => b.Height == 2);
        Assert.Equal(replacement.Hash.ToWireBytes(), tip.Hash);
        Assert.Equal(2, await db.Coins.AsNoTracking().CountAsync());
    }

    private static async Task ConnectAndSave(MiniBtcDbContext db, ChainStore store, UtxoSet utxos, Block block)
    {
        var result = utxos.Connect(block);
        Assert.True(result.IsSuccess, result.Error?.ToString());
        store.AddConnectedBlock(block, result.Value);
        await db.SaveChangesAsync();
    }
}
