using MiniBtc.Application.Abstractions;
using MiniBtc.Application.ChainSync;
using MiniBtc.Domain.Blocks;
using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Utxo;
using static MiniBtc.Domain.Tests.TestData.TestChain;

namespace MiniBtc.Application.Tests.ChainSync;

public class ChainSynchronizerTests
{
    private readonly FakeCore _core = new();
    private readonly FakeDatabase _db = new();

    private ChainSynchronizer Synchronizer() => new(
        _core.Rpc, _db.Store, _db.UnitOfWork, NetworkParams.Regtest,
        new ChainSyncOptions { WarmupRetryDelay = TimeSpan.Zero, WarmupMaxAttempts = 5 });

    [Fact]
    public async Task Catches_up_from_genesis_committing_each_block_on_its_own()
    {
        _core.Mine(5);

        var report = (await Synchronizer().SyncToTipAsync()).Value;

        Assert.Equal(5, report.ConnectedBlocks);
        Assert.Equal(new ChainTip(_core.Blocks[^1].Hash, 5), report.Tip);
        Assert.True(report.CaughtUp);
        Assert.True(_db.GenesisCommitted);
        Assert.Equal(_core.Blocks.Skip(1).Select(b => b.Hash), _db.Committed.Select(b => b.Hash));
        Assert.Equal(1 + 5, _db.Saves); // the genesis save, then one transaction per block
    }

    [Fact]
    public async Task Resumes_from_the_stored_tip_and_fetches_only_new_blocks()
    {
        _core.Mine(8);
        _db.Seed(_core.Blocks.Skip(1).Take(5)); // a previous run stored heights 1–5

        var report = (await Synchronizer().SyncToTipAsync()).Value;

        Assert.Equal(3, report.ConnectedBlocks);
        Assert.Equal(3, _core.RawBlockRequests);
        Assert.Equal(8, report.Tip.Height);
    }

    [Fact]
    public async Task Running_again_at_the_tip_does_nothing()
    {
        _core.Mine(3);
        await Synchronizer().SyncToTipAsync();
        var requests = _core.RawBlockRequests;

        var report = (await Synchronizer().SyncToTipAsync()).Value;

        Assert.Equal(0, report.ConnectedBlocks);
        Assert.Equal(requests, _core.RawBlockRequests);
    }

    [Fact]
    public async Task Picks_up_blocks_core_mined_during_the_sync()
    {
        _core.Mine(2);
        var minedOnce = false;
        _core.OnBlockchainInfo = () =>
        {
            if (minedOnce) return;
            minedOnce = true;
            _core.Mine(3); // arrives after we read Core's height the first time
        };

        var report = (await Synchronizer().SyncToTipAsync()).Value;

        Assert.Equal(5, report.Tip.Height);
        Assert.True(report.CaughtUp);
    }

    [Fact]
    public async Task Waits_for_core_to_finish_warming_up()
    {
        _core.Mine(1);
        _core.WarmupCallsLeft = 3;

        var result = await Synchronizer().SyncToTipAsync();

        Assert.True(result.IsSuccess, result.Error?.ToString());
        Assert.Equal(1, result.Value.Tip.Height);
    }

    [Fact]
    public async Task Gives_up_when_warmup_never_ends()
    {
        _core.WarmupCallsLeft = int.MaxValue;

        var ex = await Assert.ThrowsAsync<BitcoinCoreRpcException>(() => Synchronizer().SyncToTipAsync());

        Assert.Equal(RpcErrorCodes.InWarmup, ex.Code);
    }

    [Fact]
    public async Task Refuses_a_core_on_another_chain()
    {
        _core.Chain = "main";

        var result = await Synchronizer().SyncToTipAsync();

        Assert.Equal(SyncErrors.ChainMismatch, result.Error!.Code);
        Assert.Equal(0, _db.Saves);
    }

    [Fact]
    public async Task Refuses_a_core_whose_genesis_differs_from_ours()
    {
        _core.GenesisOverride = UInt256.Parse("000000000019d6689c085ae165831e934ff763ae46a2a6c172b3f1b60a8ce26f"); // mainnet's

        var result = await Synchronizer().SyncToTipAsync();

        Assert.Equal(SyncErrors.GenesisMismatch, result.Error!.Code);
        Assert.Equal(0, _db.Saves);
    }

    [Fact]
    public async Task Stops_at_a_block_our_rules_reject_keeping_everything_before_it()
    {
        _core.Mine(2);
        _core.Append(MinedBlock(_core.Blocks[^1].Hash, Coinbase(3, 50 * ConsensusConstants.Coin + 1))); // claims 1 sat too much
        _core.Mine(2);

        var result = await Synchronizer().SyncToTipAsync();

        Assert.Equal("bad-cb-amount", result.Error!.Code);
        Assert.Contains("at height 3", result.Error.Message);
        Assert.Equal(2, _db.Committed.Count);    // heights 1 and 2 are stored; nothing after the bad block
    }

    [Fact]
    public async Task Reports_bytes_it_cannot_decode()
    {
        _core.Mine(1);
        _core.RawOverride = height => height == 1 ? [0x01, 0x02] : null;

        var result = await Synchronizer().SyncToTipAsync();

        Assert.Equal(SyncErrors.UndecodableBlock, result.Error!.Code);
        Assert.Empty(_db.Committed);
    }

    [Fact]
    public async Task Reports_a_block_whose_hash_is_not_the_one_requested()
    {
        _core.Mine(2);
        var other = _core.Blocks[2];
        _core.RawOverride = height => height == 1 ? other.ToBytes() : null;

        var result = await Synchronizer().SyncToTipAsync();

        Assert.Equal(SyncErrors.BlockHashMismatch, result.Error!.Code);
    }
}
