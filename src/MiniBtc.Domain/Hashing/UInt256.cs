using System.Buffers.Binary;

namespace MiniBtc.Domain.Hashing;

/// <summary>
/// A 256-bit value as Bitcoin keeps it (Core: uint256): txids, block hashes, merkle nodes.
/// It is held in wire order, which is also its numeric order read little-endian. Wire byte 0 is
/// the least significant byte. The display form (RPC, explorers) is the same number written
/// big-endian, i.e. the 32 bytes reversed. That is the only place reversal happens.
/// </summary>
public readonly record struct UInt256
{
    public const int Size = 32;

    // Little-endian 64-bit limbs: _l0 = wire bytes 0..7 (least significant), _l3 = wire bytes 24..31.
    // Plain fields give value equality for free and set up the numeric PoW comparison later.
    private readonly ulong _l0, _l1, _l2, _l3;

    public static UInt256 Zero => default;

    /// <summary>From 32 bytes in wire order, exactly as they sit in a serialized header or tx.</summary>
    public UInt256(ReadOnlySpan<byte> wireBytes)
    {
        if (wireBytes.Length != Size)
            throw new ArgumentException($"A UInt256 is {Size} bytes, got {wireBytes.Length}.", nameof(wireBytes));
        _l0 = BinaryPrimitives.ReadUInt64LittleEndian(wireBytes);
        _l1 = BinaryPrimitives.ReadUInt64LittleEndian(wireBytes[8..]);
        _l2 = BinaryPrimitives.ReadUInt64LittleEndian(wireBytes[16..]);
        _l3 = BinaryPrimitives.ReadUInt64LittleEndian(wireBytes[24..]);
    }

    /// <summary>Writes the 32 bytes in wire order.</summary>
    public void WriteTo(Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(destination, _l0);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[8..], _l1);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[16..], _l2);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[24..], _l3);
    }

    public byte[] ToWireBytes()
    {
        var bytes = new byte[Size];
        WriteTo(bytes);
        return bytes;
    }

    /// <summary>Display form: most significant limb first, i.e. the wire bytes reversed.</summary>
    public override string ToString() => $"{_l3:x16}{_l2:x16}{_l1:x16}{_l0:x16}";

    /// <summary>Parses the display form (as getblockhash or a block explorer shows it).</summary>
    public static UInt256 Parse(string displayHex)
    {
        var bytes = Convert.FromHexString(displayHex);
        if (bytes.Length != Size)
            throw new FormatException($"A UInt256 is {Size * 2} hex characters, got {displayHex.Length}.");
        Array.Reverse(bytes);
        return new UInt256(bytes);
    }
}
