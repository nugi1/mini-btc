using MiniBtc.Domain.Blocks;
using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Tests.TestData;

namespace MiniBtc.Domain.Tests.Consensus;

public class NetworkParamsTests
{
    private static readonly NetworkParams Regtest = NetworkParams.Regtest;

    [Fact]
    public void Builds_the_regtest_genesis_block_from_its_parameters()
    {
        Assert.Equal(RegtestGenesis.Hash, Regtest.GenesisHash.ToString());
        Assert.Equal(RegtestGenesis.MerkleRoot, Regtest.GenesisBlock.Header.MerkleRoot.ToString());
        // Built from Core's recipe, it matches the captured bytes exactly.
        Assert.Equal(RegtestGenesis.Block, Regtest.GenesisBlock.ToBytes());
    }

    [Fact]
    public void Genesis_bits_are_the_compact_pow_limit()
    {
        Assert.Equal(CompactTarget.Encode(Regtest.PowLimit), Regtest.GenesisBlock.Header.Bits);
        Assert.True(Regtest.PowNoRetargeting);
    }

    // 50 BTC halves every 150 blocks; a right shift of 33 or more leaves nothing of 5,000,000,000 sat.
    [Theory]
    [InlineData(0, 5_000_000_000L)]
    [InlineData(149, 5_000_000_000L)]
    [InlineData(150, 2_500_000_000L)]
    [InlineData(299, 2_500_000_000L)]
    [InlineData(300, 1_250_000_000L)]
    [InlineData(150 * 32, 1L)]
    [InlineData(150 * 33, 0L)]
    [InlineData(150 * 64, 0L)]
    public void Subsidy_halves_every_150_blocks(int height, long subsidy)
    {
        Assert.Equal(subsidy, Regtest.GetBlockSubsidy(height));
    }

    [Fact]
    public void Address_prefixes_match_core()
    {
        Assert.Equal(0x6f, Regtest.PubKeyAddressPrefix);
        Assert.Equal(0xc4, Regtest.ScriptAddressPrefix);
        Assert.Equal(0xef, Regtest.SecretKeyPrefix);
        Assert.Equal("bcrt", Regtest.Bech32Hrp);
    }

    [Fact]
    public void Money_range_is_zero_to_21_million_btc()
    {
        Assert.True(ConsensusConstants.IsMoneyRange(0));
        Assert.True(ConsensusConstants.IsMoneyRange(2_100_000_000_000_000));
        Assert.False(ConsensusConstants.IsMoneyRange(2_100_000_000_000_001));
        Assert.False(ConsensusConstants.IsMoneyRange(-1));
        Assert.Equal(100, ConsensusConstants.CoinbaseMaturity);
    }
}
