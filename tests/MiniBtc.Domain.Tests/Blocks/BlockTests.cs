using MiniBtc.Domain.Blocks;
using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Serialization;
using MiniBtc.Domain.Tests.TestData;

namespace MiniBtc.Domain.Tests.Blocks;

public class BlockTests
{
    [Fact]
    public void Regtest_genesis_block_decodes_hashes_and_round_trips()
    {
        var bytes = RegtestGenesis.Block;

        var block = Block.Parse(bytes);

        Assert.Equal(285, bytes.Length);
        Assert.Equal(RegtestGenesis.Hash, block.Hash.ToString());
        Assert.Equal(UInt256.Zero, block.Header.PreviousBlockHash);
        Assert.Equal(1, block.Header.Version);
        Assert.Equal(1296688602u, block.Header.Time);
        Assert.Equal(RegtestGenesis.Bits, block.Header.Bits);
        Assert.Equal(2u, block.Header.Nonce);
        Assert.True(Assert.Single(block.Transactions).IsCoinbase);
        Assert.Equal(bytes, block.ToBytes());
    }

    [Fact]
    public void Merkle_root_from_the_transactions_matches_the_header()
    {
        var block = Block.Parse(RegtestGenesis.Block);

        var root = block.ComputeMerkleRoot(out var mutated);

        Assert.Equal(block.Header.MerkleRoot, root);
        Assert.Equal(RegtestGenesis.MerkleRoot, root.ToString());
        Assert.False(mutated);
    }

    [Fact]
    public void Genesis_hash_is_at_most_the_target_its_bits_encode()
    {
        var header = Block.Parse(RegtestGenesis.Block).Header;

        var target = CompactTarget.Decode(header.Bits, out _, out _);

        Assert.True(header.Hash <= target); // nonce 2 was enough: regtest's target is half of all hashes
    }

    [Fact]
    public void Header_is_80_bytes_and_hashes_to_the_block_hash()
    {
        var header = Block.Parse(RegtestGenesis.Block).Header;

        Assert.Equal(RegtestGenesis.Header, header.ToBytes());
        Assert.Equal(BlockHeader.Size, header.ToBytes().Length);
        Assert.Equal(Hashes.Hash256(RegtestGenesis.Header), header.Hash);
    }

    [Fact]
    public void Leftover_bytes_fail_like_submitblock()
    {
        byte[] bytes = [.. RegtestGenesis.Block, 0x00];

        var ex = Assert.Throws<DecodeException>(() => Block.Parse(bytes));
        Assert.Equal(DecodeReasons.BlockDecodeFailed, ex.Reason);
    }

    [Fact]
    public void Truncated_header_is_end_of_data()
    {
        var ex = Assert.Throws<DecodeException>(() => Block.Parse(RegtestGenesis.Header[..79]));
        Assert.Equal(DecodeReasons.EndOfData, ex.Reason);
    }
}
