using MiniBtc.Application.Tests.ChainSync;
using MiniBtc.Application.Unconfirmed;
using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Transactions;

namespace MiniBtc.Application.Tests.Unconfirmed;

public class MempoolSynchronizerTests
{
    private readonly FakeCore _core = new();
    private readonly MempoolState _mempool = new(NetworkParams.Regtest);

    private MempoolSynchronizer Synchronizer() => new(_core.Rpc, _mempool);

    /// <summary>A transaction Core holds; its contents don't matter here, only its identity.</summary>
    private static Transaction Tx(byte tag) => new(2,
        [new TxIn(new OutPoint(Hashes.Hash256([tag]), 0), ReadOnlySpan<byte>.Empty)],
        [new TxOut(1_000, [0x51])], 0);

    [Fact]
    public async Task A_reload_takes_cores_mempool_and_its_sequence()
    {
        _core.Mempool.AddRange([Tx(1), Tx(2)]);
        _core.MempoolSequence = 10;

        var count = await Synchronizer().ReloadAsync();

        Assert.Equal(2, count);
        Assert.Equal(10ul, _mempool.MempoolSequence);
        Assert.True(_mempool.Read(view => view.Contains(Tx(2).Txid)));
    }

    [Fact]
    public async Task Events_the_reload_already_covers_are_skipped()
    {
        _core.MempoolSequence = 10;
        await Synchronizer().ReloadAsync();
        _core.Mempool.Add(Tx(3));

        Assert.False(await Synchronizer().AddedAsync(Tx(3).Txid, 10));
        Assert.True(await Synchronizer().AddedAsync(Tx(3).Txid, 11));
        Assert.True(_mempool.Read(view => view.Contains(Tx(3).Txid)));
    }

    [Fact]
    public async Task A_removal_takes_the_transaction_out()
    {
        _core.Mempool.Add(Tx(4));
        await Synchronizer().AddedAsync(Tx(4).Txid, 1);

        Assert.True(Synchronizer().Removed(Tx(4).Txid, 2));

        Assert.Equal(0, _mempool.Read(view => view.Count));
    }

    [Fact]
    public async Task A_transaction_core_no_longer_has_is_skipped_but_its_sequence_counts()
    {
        Assert.False(await Synchronizer().AddedAsync(Tx(5).Txid, 7));

        Assert.Equal(0, _mempool.Read(view => view.Count));
        Assert.Equal(7ul, _mempool.MempoolSequence);
    }
}
