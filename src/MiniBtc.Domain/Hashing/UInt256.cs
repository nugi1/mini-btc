using System.Buffers.Binary;
using System.Numerics;

namespace MiniBtc.Domain.Hashing;

/// <summary>
/// A 256-bit value as Bitcoin keeps it (Core: uint256): txids, block hashes, merkle nodes.
/// It is held in wire order, which is also its numeric order read little-endian. Wire byte 0 is
/// the least significant byte. The display form (RPC, explorers) is the same number written
/// big-endian, i.e. the 32 bytes reversed. That is the only place reversal happens.
/// </summary>
public readonly record struct UInt256 : IComparable<UInt256>
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

    /// <summary>
    /// Numeric order, as Core's arith_uint256 compares: most significant limb first. Proof of work
    /// asks whether the block hash, read as this number, is at most the target.
    /// </summary>
    public int CompareTo(UInt256 other)
    {
        var c = _l3.CompareTo(other._l3);
        if (c != 0) return c;
        c = _l2.CompareTo(other._l2);
        if (c != 0) return c;
        c = _l1.CompareTo(other._l1);
        return c != 0 ? c : _l0.CompareTo(other._l0);
    }

    public static bool operator <(UInt256 left, UInt256 right) => left.CompareTo(right) < 0;
    public static bool operator >(UInt256 left, UInt256 right) => left.CompareTo(right) > 0;
    public static bool operator <=(UInt256 left, UInt256 right) => left.CompareTo(right) <= 0;
    public static bool operator >=(UInt256 left, UInt256 right) => left.CompareTo(right) >= 0;

    public BigInteger ToBigInteger()
    {
        Span<byte> bytes = stackalloc byte[Size];
        WriteTo(bytes);
        return new BigInteger(bytes, isUnsigned: true, isBigEndian: false);
    }

    /// <summary>From a non-negative integer below 2^256.</summary>
    public static UInt256 FromBigInteger(BigInteger value)
    {
        if (value.Sign < 0 || value.GetBitLength() > Size * 8)
            throw new ArgumentOutOfRangeException(nameof(value), "A UInt256 holds 0 to 2^256 - 1.");
        Span<byte> bytes = stackalloc byte[Size];
        bytes.Clear();
        value.TryWriteBytes(bytes, out _, isUnsigned: true, isBigEndian: false);
        return new UInt256(bytes);
    }

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
