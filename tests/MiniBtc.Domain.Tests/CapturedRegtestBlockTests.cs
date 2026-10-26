using System.Text.Json;
using MiniBtc.Domain.Blocks;
using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Serialization;
using MiniBtc.Domain.Tests.TestData;
using static MiniBtc.Domain.Tests.TestData.RegtestFixtures;

namespace MiniBtc.Domain.Tests;

/// <summary>
/// E1's acceptance test: blocks captured from our own regtest Core node (scripts/capture-fixtures.sh)
/// decode, re-encode byte for byte, and every hash we compute agrees with what Core reported in
/// getblock &lt;hash&gt; 2. If our wire format or hashing were wrong anywhere, these would disagree.
/// </summary>
public class CapturedRegtestBlockTests
{
    public static TheoryData<string> Fixtures => ["genesis", "coinbase-only", "segwit-spend", "mixed"];

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Block_round_trips_byte_exact_and_hashes_to_cores_hash(string name)
    {
        var (raw, core) = Load(name);

        var block = Block.Parse(raw);

        Assert.Equal(raw, block.ToBytes());
        Assert.Equal(core.GetProperty("hash").GetString(), block.Hash.ToString());
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Header_fields_match_getblock(string name)
    {
        var (raw, core) = Load(name);

        var header = Block.Parse(raw).Header;

        Assert.Equal(core.GetProperty("version").GetInt32(), header.Version);
        Assert.Equal(
            core.TryGetProperty("previousblockhash", out var prev) ? prev.GetString() : UInt256.Zero.ToString(),
            header.PreviousBlockHash.ToString());
        Assert.Equal(core.GetProperty("merkleroot").GetString(), header.MerkleRoot.ToString());
        Assert.Equal(core.GetProperty("time").GetUInt32(), header.Time);
        Assert.Equal(core.GetProperty("bits").GetString(), header.Bits.ToString("x8"));
        Assert.Equal(core.GetProperty("nonce").GetUInt32(), header.Nonce);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Txids_wtxids_and_transaction_bytes_match_core(string name)
    {
        var (raw, core) = Load(name);

        var transactions = Block.Parse(raw).Transactions;
        var coreTransactions = core.GetProperty("tx").EnumerateArray().ToArray();

        Assert.Equal(coreTransactions.Length, transactions.Count);
        for (var i = 0; i < transactions.Count; i++)
        {
            Assert.Equal(coreTransactions[i].GetProperty("txid").GetString(), transactions[i].Txid.ToString());
            Assert.Equal(coreTransactions[i].GetProperty("hash").GetString(), transactions[i].Wtxid.ToString());
            Assert.Equal(coreTransactions[i].GetProperty("hex").GetString(), Convert.ToHexStringLower(transactions[i].ToBytes()));
            Assert.Equal(i == 0, transactions[i].IsCoinbase);
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Merkle_root_from_our_txids_matches_the_header(string name)
    {
        var (raw, _) = Load(name);
        var block = Block.Parse(raw);

        var root = block.ComputeMerkleRoot(out var mutated);

        Assert.Equal(block.Header.MerkleRoot, root);
        Assert.False(mutated);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Sizes_with_and_without_witness_match_core(string name)
    {
        var (raw, core) = Load(name);
        var block = Block.Parse(raw);

        var stripped = BlockHeader.Size
            + CompactSize.SizeOf((ulong)block.Transactions.Count)
            + block.Transactions.Sum(tx => tx.ToBytes(includeWitness: false).Length);

        Assert.Equal(core.GetProperty("size").GetInt32(), raw.Length);
        Assert.Equal(core.GetProperty("strippedsize").GetInt32(), stripped);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Hash_meets_the_target_its_bits_encode(string name)
    {
        var (raw, _) = Load(name);
        var header = Block.Parse(raw).Header;

        var target = CompactTarget.Decode(header.Bits, out var negative, out var overflow);

        Assert.False(negative);
        Assert.False(overflow);
        Assert.True(target <= NetworkParams.Regtest.PowLimit);
        Assert.True(header.Hash <= target);
    }

    [Fact]
    public void Our_genesis_block_is_byte_identical_to_cores()
    {
        var (raw, _) = Load("genesis");

        Assert.Equal(raw, NetworkParams.Regtest.GenesisBlock.ToBytes());
    }

    [Fact]
    public void Captured_blocks_cover_coinbase_witness_legacy_and_segwit_spends()
    {
        // Guards the fixtures themselves: each must show the case it was captured for.
        var coinbaseOnly = Block.Parse(Load("coinbase-only").Raw);
        Assert.True(Assert.Single(coinbaseOnly.Transactions).HasWitness); // witness reserved value

        var segwitSpend = Block.Parse(Load("segwit-spend").Raw);
        Assert.Equal(2, segwitSpend.Transactions.Count);
        Assert.True(segwitSpend.Transactions[1].HasWitness);

        var mixed = Block.Parse(Load("mixed").Raw);
        Assert.Equal(3, mixed.Transactions.Count);                       // odd: last txid pairs with itself
        Assert.Contains(mixed.Transactions.Skip(1), tx => !tx.HasWitness); // legacy P2PKH spend
        Assert.Contains(mixed.Transactions.Skip(1), tx => tx.HasWitness);  // SegWit spend
    }
}
