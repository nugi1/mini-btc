using MiniBtc.Application.Abstractions;
using MiniBtc.Application.Broadcast;
using MiniBtc.Application.Tests.ChainSync;
using MiniBtc.Application.Unconfirmed;
using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Tests.TestData;
using MiniBtc.Domain.Transactions;
using MiniBtc.Domain.Unconfirmed;
using MiniBtc.Domain.Utxo;
using static MiniBtc.Domain.Tests.TestData.TestChain;

namespace MiniBtc.Application.Tests.Broadcast;

public class TransactionBroadcasterTests
{
    private const long Btc = ConsensusConstants.Coin;
    private static readonly byte[] KeyOne = TestSigner.KeyOne.P2pkhScript;

    private readonly FakeCore _core = new();
    private readonly MempoolState _mempool = new(NetworkParams.Regtest);
    private readonly UtxoSet _utxos;
    private readonly OutPoint _coin;

    public TransactionBroadcasterTests()
    {
        var (utxos, coinbase) = ChainWithCoinbaseAtHeight1(tipHeight: 100, payTo: KeyOne);
        _utxos = utxos;
        _coin = new OutPoint(coinbase.Txid, 0);
    }

    private TransactionBroadcaster Broadcaster() => new(_core.Rpc, new FakeCoins(_utxos), _mempool);

    private static string Pay(OutPoint from, long value) => Convert.ToHexStringLower(
        TestSigner.KeyOne.SignInput(new Transaction(2, [new TxIn(from, ReadOnlySpan<byte>.Empty)], [new TxOut(value, KeyOne)], 0), 0).ToBytes());

    [Fact]
    public async Task A_valid_spend_goes_to_core_and_joins_the_mempool_view()
    {
        var result = await Broadcaster().BroadcastAsync(Pay(_coin, 49 * Btc));

        Assert.True(result.IsSuccess, result.Error?.ToString());
        Assert.Equal(1 * Btc, result.Value.Fee);
        Assert.Equal(result.Value.Txid, Assert.Single(_core.Mempool).Txid);
        Assert.True(_mempool.Read(view => view.Contains(result.Value.Txid)));
    }

    [Fact]
    public async Task A_double_spend_is_refused_by_the_node_before_core_sees_it()
    {
        await Broadcaster().BroadcastAsync(Pay(_coin, 49 * Btc));

        var result = await Broadcaster().BroadcastAsync(Pay(_coin, 48 * Btc));

        Assert.False(BroadcastErrors.IsFromCore(result.Error!));
        Assert.Equal(MempoolErrors.ReplacementDisallowed, BroadcastErrors.Reason(result.Error!));
        Assert.Equal(1, _core.SendRequests);
    }

    [Fact]
    public async Task An_immature_coinbase_is_refused_by_the_node()
    {
        var (young, coinbase) = ChainWithCoinbaseAtHeight1(tipHeight: 99, payTo: KeyOne);
        var broadcaster = new TransactionBroadcaster(_core.Rpc, new FakeCoins(young), _mempool);

        var result = await broadcaster.BroadcastAsync(Pay(new OutPoint(coinbase.Txid, 0), 49 * Btc));

        Assert.Equal("bad-txns-premature-spend-of-coinbase", result.Error!.Code);
        Assert.Equal(0, _core.SendRequests);
    }

    [Fact]
    public async Task What_core_refuses_is_reported_as_cores_refusal_and_stays_out_of_the_view()
    {
        _core.RejectSends = (RpcErrorCodes.TransactionRejected, "min relay fee not met, 0 < 192");

        var result = await Broadcaster().BroadcastAsync(Pay(_coin, 49 * Btc));

        Assert.True(BroadcastErrors.IsFromCore(result.Error!));
        Assert.Equal("min relay fee not met", BroadcastErrors.Reason(result.Error!));
        Assert.Contains("RPC -26", result.Error!.Message);
        Assert.Equal(0, _mempool.Read(view => view.Count));
    }

    [Theory]
    [InlineData("not hex")]
    [InlineData("0200")]
    public async Task Bytes_that_are_not_a_transaction_fail_with_cores_decode_message(string hex)
    {
        var result = await Broadcaster().BroadcastAsync(hex);

        Assert.Equal(BroadcastErrors.DecodeFailed, result.Error!.Code);
        Assert.Equal("TX decode failed. Make sure the tx has at least one input.", result.Error.Message);
    }

    private sealed class FakeCoins(UtxoSet utxos) : ICoinLookup
    {
        public Task<CoinSnapshot> GetCoinsAsync(IReadOnlyCollection<OutPoint> outPoints, CancellationToken cancellationToken = default)
        {
            var found = outPoints
                .Select(outPoint => (outPoint, coin: utxos.GetCoin(outPoint)))
                .Where(pair => pair.coin is not null)
                .ToDictionary(pair => pair.outPoint, pair => pair.coin!);
            return Task.FromResult(new CoinSnapshot(utxos.Tip.Height, found));
        }
    }
}
