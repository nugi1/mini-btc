using MiniBtc.Domain.Blocks;
using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Tests.TestData;
using MiniBtc.Domain.Transactions;
using MiniBtc.Domain.Utxo;
using MiniBtc.Domain.Validation;
using static MiniBtc.Domain.Tests.TestData.TestChain;

namespace MiniBtc.Domain.Tests.Validation;

public class HeaderRulesTests
{
    private static readonly ChainTip AtGenesis = new(Genesis, 0);

    public static TheoryData<string> CapturedBlocks => [.. RegtestFixtures.Names];

    [Theory]
    [MemberData(nameof(CapturedBlocks))]
    public void Blocks_mined_by_core_pass_every_header_rule(string name)
    {
        var (raw, core) = RegtestFixtures.Load(name);
        var block = Block.Parse(raw);
        var tip = new ChainTip(block.Header.PreviousBlockHash, core.GetProperty("height").GetInt32() - 1);

        var result = HeaderRules.All.Check(new BlockContext(block, tip, NetworkParams.Regtest));

        Assert.True(result.IsSuccess, result.Error?.ToString());
    }

    [Fact]
    public void Hash_above_the_target_is_high_hash()
    {
        var coinbase = Coinbase(1);
        var block = new Block(MineBadly(Genesis, coinbase.Txid, RegtestBits), [coinbase]);

        AssertRejected("high-hash", block, AtGenesis);
    }

    [Theory]
    [InlineData(0x2100ffffU)] // decodes to ffff00…00: easier than regtest's powLimit allows
    [InlineData(0x04923456U)] // sign bit set: negative target
    [InlineData(0x03000000U)] // zero target
    [InlineData(0xff123456U)] // overflows 256 bits
    public void Bits_outside_the_valid_range_are_high_hash(uint bits)
    {
        var coinbase = Coinbase(1);
        // Nonce 0 is enough: the bits fail on their own, whatever the hash.
        var header = new BlockHeader(0x20000000, Genesis, coinbase.Txid, 1_700_000_000, bits, 0);

        AssertRejected("high-hash", new Block(header, [coinbase]), AtGenesis);
    }

    [Fact]
    public void Block_on_another_parent_is_prev_blk_not_found()
    {
        var block = MinedBlock(Genesis, Coinbase(1));
        var elsewhere = new ChainTip(UInt256.Parse(RegtestGenesis.MerkleRoot), 0);

        AssertRejected("prev-blk-not-found", block, elsewhere);
    }

    [Fact]
    public void Header_committing_to_other_transactions_is_bad_txnmrklroot()
    {
        var block = MinedBlockWithMerkleRoot(Genesis, Coinbase(99).Txid, Coinbase(1));

        AssertRejected("bad-txnmrklroot", block, AtGenesis);
    }

    [Fact]
    public void Duplicated_last_transaction_is_bad_txns_duplicate_even_though_the_root_matches()
    {
        Transaction[] three = [Coinbase(1), Spend([new OutPoint(Genesis, 1)], 1), Spend([new OutPoint(Genesis, 2)], 1)];
        var root = MerkleTree.ComputeRoot([.. three.Select(tx => tx.Txid)], out _);
        var mutated = MinedBlockWithMerkleRoot(Genesis, root, [.. three, three[^1]]);

        Assert.Equal(root, mutated.ComputeMerkleRoot(out _)); // CVE-2012-2459: same root
        AssertRejected("bad-txns-duplicate", mutated, AtGenesis);
    }

    [Fact]
    public void Proof_of_work_is_checked_before_the_parent_and_the_merkle_root()
    {
        // Wrong parent, wrong merkle root and no proof of work: Core reports high-hash first.
        var header = MineBadly(UInt256.Zero, UInt256.Zero, RegtestBits);

        AssertRejected("high-hash", new Block(header, [Coinbase(1)]), AtGenesis);
    }

    [Fact]
    public void The_parent_is_checked_before_the_merkle_root()
    {
        var block = MinedBlockWithMerkleRoot(UInt256.Zero, UInt256.Zero, Coinbase(1));

        AssertRejected("prev-blk-not-found", block, AtGenesis);
    }

    private static void AssertRejected(string code, Block block, ChainTip tip)
    {
        var result = HeaderRules.All.Check(new BlockContext(block, tip, NetworkParams.Regtest));

        Assert.True(result.IsFailure, $"expected {code}, but the block passed");
        Assert.Equal(code, result.Error!.Code);
    }
}
