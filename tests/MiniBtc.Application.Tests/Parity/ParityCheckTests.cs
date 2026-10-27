using MiniBtc.Application.Abstractions;
using MiniBtc.Application.Parity;
using MiniBtc.Application.Tests.ChainSync;
using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Transactions;
using MiniBtc.Domain.Utxo;

namespace MiniBtc.Application.Tests.Parity;

public class ParityCheckTests
{
    private const string KeyOne = "mrCDrCybB6J1vRfbwM5hemdJz73FwDBC8r";
    private static readonly byte[] KeyOneScript = Convert.FromHexString("76a914751e76e8199196d454941c45d1b3a323f1433bd688ac");
    private static readonly UInt256 Tip = Hashes.Hash256([1]);

    private readonly FakeCore _core = new();

    private ParityCheck Check(UtxoSummary ours, params long[] keyOneCoins) =>
        new(_core.Rpc, new FakeStats(ours), new FakeIndex(ours.TipHeight, keyOneCoins), NetworkParams.Regtest);

    [Fact]
    public async Task Agreement_on_every_line_is_green()
    {
        _core.UtxoSetInfo = new TxOutSetInfo(110, Tip, 110, 5_500 * ConsensusConstants.Coin);
        _core.Scans[$"addr({KeyOne})"] = new TxOutSetScan(110, Tip, 2, 100 * ConsensusConstants.Coin);

        var report = await Check(new UtxoSummary(110, Tip, 110, 5_500 * ConsensusConstants.Coin), 50 * ConsensusConstants.Coin, 50 * ConsensusConstants.Coin)
            .RunAsync([KeyOne]);

        Assert.True(report.Green);
        Assert.True(report.Level);
        Assert.Equal(5, report.Checks.Count);
    }

    [Fact]
    public async Task One_coin_less_than_core_turns_the_count_line_red()
    {
        _core.UtxoSetInfo = new TxOutSetInfo(110, Tip, 111, 5_550 * ConsensusConstants.Coin);

        var report = await Check(new UtxoSummary(110, Tip, 110, 5_500 * ConsensusConstants.Coin)).RunAsync([]);

        Assert.False(report.Green);
        Assert.True(report.Level); // same tip: the difference is real, not a sync lag
        var count = Assert.Single(report.Checks, c => c.Name == "UTXO count");
        Assert.Equal(("110", "111", false), (count.Ours, count.Core, count.Match));
    }

    [Fact]
    public async Task An_invalid_address_is_a_red_line_with_cores_message()
    {
        _core.UtxoSetInfo = new TxOutSetInfo(0, Tip, 0, 0);

        var report = await Check(new UtxoSummary(0, Tip, 0, 0)).RunAsync(["mrCDrCybB6J1vRfbwM5hemdJz73FwDBC8s"]);

        var line = report.Checks[^1];
        Assert.False(line.Match);
        Assert.Equal("Invalid checksum or length of Base58 address (P2PKH or P2SH)", line.Ours);
    }

    private sealed class FakeStats(UtxoSummary summary) : IUtxoStats
    {
        public Task<UtxoSummary> GetSummaryAsync(CancellationToken cancellationToken = default) => Task.FromResult(summary);
    }

    private sealed class FakeIndex(int tipHeight, long[] values) : IAddressIndex
    {
        public Task<IndexedCoins> GetCoinsAsync(byte[] scriptPubKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(new IndexedCoins(tipHeight, values.Select((value, i) => new KeyValuePair<OutPoint, Coin>(
                new OutPoint(Hashes.Hash256([(byte)i]), 0), new Coin(new TxOut(value, KeyOneScript), i + 1, true))).ToList()));
    }
}
