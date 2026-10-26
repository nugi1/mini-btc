using MiniBtc.Domain.Hashing;

namespace MiniBtc.Domain.Blocks;

/// <summary>
/// Bitcoin's merkle tree (Core: ComputeMerkleRoot in consensus/merkle.cpp). Each parent is
/// HASH256 of its two children's 32 bytes concatenated in wire order. A level with an odd count
/// pairs its last hash with itself.
/// </summary>
public static class MerkleTree
{
    /// <summary>
    /// <paramref name="mutated"/> is set when some level has two equal hashes in one pair. Because of
    /// last-hash duplication, [a, b, c] and [a, b, c, c] share a root (CVE-2012-2459); Core uses
    /// this flag to reject the duplicated form as bad-txns-duplicate instead of marking the
    /// real block invalid.
    /// </summary>
    public static UInt256 ComputeRoot(IReadOnlyList<UInt256> leaves, out bool mutated)
    {
        mutated = false;
        if (leaves.Count == 0) return UInt256.Zero;

        var level = new List<UInt256>(leaves);
        Span<byte> pair = stackalloc byte[2 * UInt256.Size];
        while (level.Count > 1)
        {
            for (var pos = 0; pos + 1 < level.Count; pos += 2)
            {
                if (level[pos] == level[pos + 1]) mutated = true;
            }

            if (level.Count % 2 == 1) level.Add(level[^1]);

            var parents = new List<UInt256>(level.Count / 2);
            for (var i = 0; i < level.Count; i += 2)
            {
                level[i].WriteTo(pair);
                level[i + 1].WriteTo(pair[UInt256.Size..]);
                parents.Add(Hashes.Hash256(pair));
            }
            level = parents;
        }

        return level[0];
    }
}
