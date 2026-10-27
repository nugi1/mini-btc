using MiniBtc.Application.Abstractions;
using MiniBtc.Application.Addresses;
using MiniBtc.Domain.Addresses;
using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Transactions;
using MiniBtc.Domain.Utxo;

namespace MiniBtc.Application.Tests.Addresses;

public class AddressQueriesTests
{
    private const long Btc = ConsensusConstants.Coin;
    private const string KeyOne = "mrCDrCybB6J1vRfbwM5hemdJz73FwDBC8r";
    private static readonly byte[] KeyOneScript = Convert.FromHexString("76a914751e76e8199196d454941c45d1b3a323f1433bd688ac");

    [Fact]
    public async Task After_101_blocks_to_one_address_the_two_oldest_coinbases_are_spendable()
    {
        // Coinbases at heights 1..101, tip 101. The next block is 102, and a coinbase from height h
        // may be spent there if 102 - h >= 100: heights 1 and 2. (Core's wallet waits one block
        // longer before showing a coinbase as spendable; consensus and the mempool don't.)
        var index = new FakeIndex(101, Enumerable.Range(1, 101).Select(h => Coinbase(h)).ToArray());
        var queries = new AddressQueries(index, NetworkParams.Regtest);

        var balance = (await queries.GetBalanceAsync(KeyOne)).Value;
        var utxos = (await queries.GetUtxosAsync(KeyOne)).Value;

        Assert.Equal((2 * 50 * Btc, 99 * 50 * Btc, 101 * 50 * Btc, 101), (balance.Spendable, balance.Immature, balance.Total, balance.UtxoCount));
        Assert.Equal(new[] { 1, 2 }, utxos.Utxos.Where(u => u.IsSpendable).Select(u => u.Height));
        Assert.Equal(KeyOneScript, index.LastScript);
    }

    [Fact]
    public async Task Each_utxo_reports_confirmations_and_when_it_matures()
    {
        var index = new FakeIndex(150, Coinbase(51), Coinbase(60), Payment(149));
        var utxos = (await new AddressQueries(index, NetworkParams.Regtest).GetUtxosAsync(KeyOne)).Value;

        Assert.Equal(150, utxos.TipHeight);
        Assert.Equal(KeyOne, utxos.Address.Text);
        Assert.Collection(utxos.Utxos,
            u => Assert.Equal((100, true, true, (int?)151), (u.Confirmations, u.IsCoinbase, u.IsSpendable, u.MaturityHeight)),
            u => Assert.Equal((91, true, false, (int?)160), (u.Confirmations, u.IsCoinbase, u.IsSpendable, u.MaturityHeight)),
            u => Assert.Equal((2, false, true, (int?)null), (u.Confirmations, u.IsCoinbase, u.IsSpendable, u.MaturityHeight)));
    }

    [Fact]
    public async Task A_bad_address_fails_with_cores_message_without_touching_the_index()
    {
        var index = new FakeIndex(0);

        var result = await new AddressQueries(index, NetworkParams.Regtest).GetBalanceAsync("mrCDrCybB6J1vRfbwM5hemdJz73FwDBC8s");

        Assert.True(result.IsFailure);
        Assert.Equal(AddressErrors.InvalidChecksumOrLength, result.Error!.Message);
        Assert.Null(index.LastScript);
    }

    private static KeyValuePair<OutPoint, Coin> Coinbase(int height) => Entry(height, isCoinbase: true);

    private static KeyValuePair<OutPoint, Coin> Payment(int height) => Entry(height, isCoinbase: false);

    private static KeyValuePair<OutPoint, Coin> Entry(int height, bool isCoinbase) => new(
        new OutPoint(Hashes.Hash256(BitConverter.GetBytes(height)), 0),
        new Coin(new TxOut(50 * Btc, KeyOneScript), height, isCoinbase));

    private sealed class FakeIndex(int tipHeight, params KeyValuePair<OutPoint, Coin>[] coins) : IAddressIndex
    {
        public byte[]? LastScript { get; private set; }

        public Task<IndexedCoins> GetCoinsAsync(byte[] scriptPubKey, CancellationToken cancellationToken = default)
        {
            LastScript = scriptPubKey;
            return Task.FromResult(new IndexedCoins(tipHeight, coins));
        }
    }
}
