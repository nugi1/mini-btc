using Microsoft.EntityFrameworkCore;
using MiniBtc.Application.ChainSync;
using MiniBtc.Application.Unconfirmed;
using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Hashing;
using MiniBtc.Infrastructure.Persistence;
using MiniBtc.Integration.Tests.Persistence;

namespace MiniBtc.Integration.Tests.BitcoinCore;

/// <summary>
/// E4's reorg acceptance test against the compose bitcoind. A throwaway database syncs Core's whole
/// chain with the node's own code; then Core is made to drop its tip (invalidateblock), and the node
/// must follow it back; then Core mines a longer branch, and the node must follow it forward. After
/// each step our UTXO set must equal Core's, by count and by total value (gettxoutsetinfo).
/// Note: this mines three blocks on your regtest chain and leaves one of them invalidated-then-reconsidered.
/// </summary>
public sealed class ReorgAgainstCoreTests : IAsyncLifetime
{
    private readonly TestDatabase _database = new();
    private readonly CoreTestRpc _core = new();
    private readonly MempoolState _mempool = new(NetworkParams.Regtest);

    public Task InitializeAsync() => _database.InitializeAsync();

    public async Task DisposeAsync()
    {
        _core.Dispose();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task Invalidateblock_rolls_tip_and_utxo_set_back_and_re_mining_reconnects()
    {
        await SyncAsync();
        await AssertSameUtxoSetAsCoreAsync();

        var dropped = (await _core.MineAsync(1))[0];
        Assert.Equal(dropped, (await SyncAsync()).Tip.Hash.ToString());

        await _core.CallAsync("invalidateblock", dropped);
        try
        {
            var back = await SyncAsync();
            Assert.Equal(1, back.DisconnectedBlocks);
            await AssertSameUtxoSetAsCoreAsync();
            var droppedHash = UInt256.Parse(dropped).ToWireBytes();
            await using (var db = _database.CreateContext())
                Assert.False(await db.Blocks.AnyAsync(b => b.Hash == droppedHash));

            // To another address: re-mined to the same one within the same second, the first block would be
            // byte-identical to the invalidated one (same parent, coinbase, time and nonce search), and Core
            // refuses a block it already marked invalid: "ProcessNewBlock, block not accepted".
            var branch = await _core.MineAsync(2, CoreTestRpc.KeyTwoAddress);
            var forward = await SyncAsync();
            Assert.Equal((0, 2), (forward.DisconnectedBlocks, forward.ConnectedBlocks));
            Assert.Equal(branch[^1], forward.Tip.Hash.ToString());
            await AssertSameUtxoSetAsCoreAsync();
        }
        finally
        {
            // Lift the invalid mark. The new branch has more work, so Core stays on it.
            await _core.CallAsync("reconsiderblock", dropped);
        }
    }

    /// <summary>One sync run with a fresh context, as the node's worker does it.</summary>
    private async Task<SyncReport> SyncAsync()
    {
        await using var db = _database.CreateContext();
        var synchronizer = new ChainSynchronizer(
            CoreTestRpc.NodeClient(), new ChainStore(db), db, NetworkParams.Regtest, new ChainSyncOptions(), _mempool);
        var result = await synchronizer.SyncToTipAsync();
        Assert.True(result.IsSuccess, result.Error?.ToString());
        return result.Value;
    }

    private async Task AssertSameUtxoSetAsCoreAsync()
    {
        var core = await _core.CallAsync("gettxoutsetinfo");
        await using var db = _database.CreateContext();
        var state = await db.ChainStates.SingleAsync();

        Assert.Equal(core.GetProperty("height").GetInt32(), state.TipHeight);
        Assert.Equal(core.GetProperty("bestblock").GetString(), new UInt256(state.TipHash).ToString());
        Assert.Equal(core.GetProperty("txouts").GetInt64(), await db.Coins.LongCountAsync());
        var coreTotal = (long)decimal.Round(core.GetProperty("total_amount").GetDecimal() * ConsensusConstants.Coin);
        Assert.Equal(coreTotal, await db.Coins.SumAsync(c => c.Value));
    }
}
