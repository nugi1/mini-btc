using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Serialization;

namespace MiniBtc.Domain.Blocks;

/// <summary>
/// The 80-byte block header, a value object (Core: CBlockHeader):
/// <code>
///   version 4 (int32) | prev block hash 32 | merkle root 32 | time 4 | bits 4 | nonce 4
/// </code>
/// The block hash is HASH256 of exactly these 80 bytes. It covers the transactions only
/// through the merkle root.
/// </summary>
public sealed class BlockHeader
{
    public const int Size = 80;

    public BlockHeader(int version, UInt256 previousBlockHash, UInt256 merkleRoot, uint time, uint bits, uint nonce)
    {
        Version = version;
        PreviousBlockHash = previousBlockHash;
        MerkleRoot = merkleRoot;
        Time = time;
        Bits = bits;
        Nonce = nonce;
        Hash = Hashes.Hash256(ToBytes());
    }

    /// <summary>Signed in Core (int32_t nVersion), unlike the transaction version.</summary>
    public int Version { get; }
    public UInt256 PreviousBlockHash { get; }
    public UInt256 MerkleRoot { get; }

    /// <summary>Unix time in seconds, as claimed by the miner.</summary>
    public uint Time { get; }

    /// <summary>The proof-of-work target in compact form; see <see cref="CompactTarget"/>.</summary>
    public uint Bits { get; }
    public uint Nonce { get; }
    public UInt256 Hash { get; }

    public static BlockHeader ReadFrom(ByteReader reader) => new(
        reader.ReadInt32(),
        reader.ReadUInt256(),
        reader.ReadUInt256(),
        reader.ReadUInt32(),
        reader.ReadUInt32(),
        reader.ReadUInt32());

    public void WriteTo(ByteWriter writer)
    {
        writer.WriteInt32(Version);
        writer.WriteUInt256(PreviousBlockHash);
        writer.WriteUInt256(MerkleRoot);
        writer.WriteUInt32(Time);
        writer.WriteUInt32(Bits);
        writer.WriteUInt32(Nonce);
    }

    public byte[] ToBytes()
    {
        var writer = new ByteWriter(Size);
        WriteTo(writer);
        return writer.ToArray();
    }
}
