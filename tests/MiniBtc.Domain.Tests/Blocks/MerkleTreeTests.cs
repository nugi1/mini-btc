using MiniBtc.Domain.Blocks;
using MiniBtc.Domain.Hashing;

namespace MiniBtc.Domain.Tests.Blocks;

public class MerkleTreeTests
{
    // Mainnet block 100000: four txids and the merkle root in its header.
    private static readonly UInt256[] Block100000Txids =
    [
        UInt256.Parse("8c14f0db3df150123e6f3dbbf30f8b955a8249b62ac1d1ff16284aefa3d06d87"),
        UInt256.Parse("fff2525b8931402dd09222c50775608f75787bd2b87e56995a7bdd30f79702c4"),
        UInt256.Parse("6359f0868171b1d194cbee1af2f16ea598ae8fad666d9b012c8ed2b79a236ec4"),
        UInt256.Parse("e9a66845e05d5abc0ad04ec80f774a7e585c6e8db975962d069a522137b80c1d"),
    ];

    private const string Block100000MerkleRoot = "f3e94742aca4b5ef85488dc37c06c3282295ffec960994b2c0d5ac2a25a95766";

    [Fact]
    public void Mainnet_block_100000_root_matches_its_header()
    {
        var root = MerkleTree.ComputeRoot(Block100000Txids, out var mutated);

        Assert.Equal(Block100000MerkleRoot, root.ToString());
        Assert.False(mutated);
    }

    [Fact]
    public void Parent_is_hash256_of_both_children_in_wire_order()
    {
        var (left, right) = (Block100000Txids[0], Block100000Txids[1]);

        var root = MerkleTree.ComputeRoot([left, right], out _);

        // Concatenate the wire bytes, not the reversed display hex.
        Assert.Equal(Hashes.Hash256([.. left.ToWireBytes(), .. right.ToWireBytes()]), root);
    }

    [Fact]
    public void One_leaf_is_its_own_root_and_no_leaves_is_zero()
    {
        Assert.Equal(Block100000Txids[0], MerkleTree.ComputeRoot([Block100000Txids[0]], out _));
        Assert.Equal(UInt256.Zero, MerkleTree.ComputeRoot([], out _));
    }

    [Fact]
    public void Odd_level_duplicates_its_last_hash_and_the_duplicate_form_is_flagged()
    {
        UInt256[] three = [.. Block100000Txids[..3]];
        UInt256[] threeWithLastRepeated = [.. three, three[^1]];

        var root = MerkleTree.ComputeRoot(three, out var mutated);
        var sameRoot = MerkleTree.ComputeRoot(threeWithLastRepeated, out var duplicateMutated);

        Assert.Equal(root, sameRoot);    // CVE-2012-2459: two tx lists, one root
        Assert.False(mutated);
        Assert.True(duplicateMutated);   // Core rejects this form as bad-txns-duplicate
    }
}
