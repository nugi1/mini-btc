using MiniBtc.Domain.Blocks;
using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Tests.TestData;
using MiniBtc.Domain.Transactions;
using MiniBtc.Domain.Utxo;
using MiniBtc.Domain.Validation;
using static MiniBtc.Domain.Tests.TestData.TestChain;

namespace MiniBtc.Domain.Tests.Validation;

public class BlockRulesTests
{
    private const long Btc = ConsensusConstants.Coin;
    private static readonly ChainTip AtGenesis = new(Genesis, 0);

    public static TheoryData<string> CapturedBlocks => [.. RegtestFixtures.Names];

    [Theory]
    [MemberData(nameof(CapturedBlocks))]
    public void Blocks_mined_by_core_have_the_right_structure(string name)
    {
        var block = Block.Parse(RegtestFixtures.Load(name).Raw);

        Assert.True(BlockRules.Structure.Check(new BlockContext(block, AtGenesis, NetworkParams.Regtest)).IsSuccess);
    }

    [Fact]
    public void Cores_coinbase_claims_exactly_subsidy_plus_fees_and_not_a_satoshi_more()
    {
        // The "mixed" block at height 103: Core's miner claimed the full reward. Fees come from
        // getblock 2, which reports each non-coinbase tx's fee in BTC.
        var (raw, core) = RegtestFixtures.Load("mixed");
        var block = Block.Parse(raw);
        var height = core.GetProperty("height").GetInt32();
        var fees = core.GetProperty("tx").EnumerateArray().Skip(1)
            .Sum(tx => (long)(tx.GetProperty("fee").GetDecimal() * Btc));

        var exact = new CoinbaseContext(block, height, fees, NetworkParams.Regtest);
        var oneSatoshiShort = exact with { Fees = fees - 1 };

        Assert.Equal(exact.Limit, block.Transactions[0].Outputs.Sum(output => output.Value));
        Assert.True(BlockRules.CoinbaseAmount.Check(exact).IsSuccess);
        Assert.Equal("bad-cb-amount", BlockRules.CoinbaseAmount.Check(oneSatoshiShort).Error!.Code);
    }

    [Fact]
    public void A_block_without_transactions_is_bad_blk_length()
    {
        var empty = new Block(Mine(Genesis, UInt256.Zero, RegtestBits), []);

        AssertStructureRejected("bad-blk-length", empty);
    }

    [Fact]
    public void A_block_not_starting_with_a_coinbase_is_bad_cb_missing()
    {
        var block = MinedBlock(Genesis, Spend([new OutPoint(Genesis, 1)], 1));

        AssertStructureRejected("bad-cb-missing", block);
    }

    [Fact]
    public void A_second_coinbase_is_bad_cb_multiple()
    {
        var block = MinedBlock(Genesis, Coinbase(1), Coinbase(2));

        AssertStructureRejected("bad-cb-multiple", block);
    }

    [Fact]
    public void A_coinbase_claiming_one_satoshi_over_the_subsidy_is_bad_cb_amount()
    {
        var utxos = UtxoSet.AtGenesis(NetworkParams.Regtest);

        var result = utxos.Connect(MinedBlock(Genesis, Coinbase(1, 50 * Btc + 1)));

        Assert.Equal("bad-cb-amount", result.Error!.Code);
        Assert.Equal("coinbase pays too much (actual=5000000001 vs limit=5000000000)", result.Error.Message);
        Assert.Empty(utxos.Coins);
    }

    [Fact]
    public void Fees_raise_what_the_coinbase_may_claim()
    {
        var (utxos, coinbase) = ChainWithCoinbaseAtHeight1();
        var payment = Spend([new OutPoint(coinbase.Txid, 0)], 49 * Btc); // pays a 1 BTC fee

        var greedy = utxos.Connect(MinedBlock(utxos.Tip.Hash, Coinbase(101, 51 * Btc + 1), payment));
        var exact = utxos.Connect(MinedBlock(utxos.Tip.Hash, Coinbase(101, 51 * Btc), payment));

        Assert.Equal("bad-cb-amount", greedy.Error!.Code);
        Assert.True(exact.IsSuccess, exact.Error?.ToString());
    }

    [Fact]
    public void The_subsidy_halves_at_height_150_on_regtest()
    {
        var (utxos, _) = ChainWithCoinbaseAtHeight1(tipHeight: 149);

        var oldReward = utxos.Connect(MinedBlock(utxos.Tip.Hash, Coinbase(150, 50 * Btc)));
        var newReward = utxos.Connect(MinedBlock(utxos.Tip.Hash, Coinbase(150, 25 * Btc)));

        Assert.Equal("bad-cb-amount", oldReward.Error!.Code);
        Assert.True(newReward.IsSuccess, newReward.Error?.ToString());
    }

    [Fact]
    public void Claiming_less_than_allowed_is_fine()
    {
        var utxos = UtxoSet.AtGenesis(NetworkParams.Regtest);

        Assert.True(utxos.Connect(MinedBlock(Genesis, Coinbase(1, 1))).IsSuccess);
    }

    [Fact]
    public void Structure_is_checked_before_each_transaction()
    {
        // No leading coinbase and a negative output: CheckBlock reports the coinbase problem first.
        var negative = new Transaction(2, [new TxIn(new OutPoint(Genesis, 1), ReadOnlySpan<byte>.Empty)],
            [new TxOut(-1, AnyoneCanSpend)], 0);
        var utxos = UtxoSet.AtGenesis(NetworkParams.Regtest);

        var result = utxos.Connect(MinedBlock(Genesis, negative));

        Assert.Equal("bad-cb-missing", result.Error!.Code);
    }

    private static void AssertStructureRejected(string code, Block block)
    {
        var result = BlockRules.Structure.Check(new BlockContext(block, AtGenesis, NetworkParams.Regtest));
        Assert.True(result.IsFailure, $"expected {code}, but the block passed");
        Assert.Equal(code, result.Error!.Code);
    }
}
