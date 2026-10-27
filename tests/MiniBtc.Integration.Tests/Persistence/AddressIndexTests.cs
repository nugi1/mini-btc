using MiniBtc.Domain.Blocks;
using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Utxo;
using MiniBtc.Infrastructure.Persistence;
using static MiniBtc.Domain.Tests.TestData.TestChain;

namespace MiniBtc.Integration.Tests.Persistence;

public sealed class AddressIndexTests : IAsyncLifetime
{
    // P2PKH of private key 1 (mrCDrCybB6J1vRfbwM5hemdJz73FwDBC8r) and of private key 2.
    private static readonly byte[] KeyOne = Convert.FromHexString("76a914751e76e8199196d454941c45d1b3a323f1433bd688ac");
    private static readonly byte[] KeyTwo = Convert.FromHexString("76a91406afd46bcdfd22ef94ac122aa11f241244a37ecc88ac");

    private readonly TestDatabase _database = new();

    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task Finds_an_addresss_coins_by_script_with_the_tip_they_reflect()
    {
        await using (var db = _database.CreateContext())
        {
            var store = new ChainStore(db);
            var utxos = await store.LoadAsync(NetworkParams.Regtest);
            await db.SaveChangesAsync();
            await ConnectAndSave(db, store, utxos, MinedBlock(utxos.Tip.Hash, CoinbasePayingTo(1, KeyOne)));
            await ConnectAndSave(db, store, utxos, MinedBlock(utxos.Tip.Hash, CoinbasePayingTo(2, KeyTwo)));
            await ConnectAndSave(db, store, utxos, MinedBlock(utxos.Tip.Hash, CoinbasePayingTo(3, KeyOne)));
        }

        await using var reader = _database.CreateContext();
        var found = await new AddressIndex(reader, NetworkParams.Regtest).GetCoinsAsync(KeyOne);

        Assert.Equal(3, found.TipHeight);
        Assert.Equal(new[] { 1, 3 }, found.Coins.Select(c => c.Value.Height));
        Assert.All(found.Coins, c =>
        {
            Assert.Equal(KeyOne, c.Value.Output.ScriptPubKey.ToArray());
            Assert.True(c.Value.IsCoinbase);
            Assert.Equal(50 * ConsensusConstants.Coin, c.Value.Value);
            Assert.Equal(0u, c.Key.Index);
        });
        Assert.Equal(CoinbasePayingTo(1, KeyOne).Txid, found.Coins[0].Key.Txid);
    }

    [Fact]
    public async Task An_empty_database_has_no_coins_at_height_0()
    {
        await using var db = _database.CreateContext();

        var found = await new AddressIndex(db, NetworkParams.Regtest).GetCoinsAsync(KeyOne);

        Assert.Equal(0, found.TipHeight);
        Assert.Empty(found.Coins);
    }

    private static async Task ConnectAndSave(MiniBtcDbContext db, ChainStore store, UtxoSet utxos, Block block)
    {
        var result = utxos.Connect(block);
        Assert.True(result.IsSuccess, result.Error?.ToString());
        store.AddConnectedBlock(block, result.Value);
        await db.SaveChangesAsync();
    }
}
