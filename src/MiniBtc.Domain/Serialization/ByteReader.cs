using System.Buffers.Binary;
using MiniBtc.Domain.Hashing;

namespace MiniBtc.Domain.Serialization;

/// <summary>
/// Forward-only reader for Bitcoin's wire format. Every fixed-width integer is little-endian;
/// every count and length is a <see cref="CompactSize"/>. Reading past the end, or a malformed
/// CompactSize, throws <see cref="DecodeException"/> with Core's reason.
/// </summary>
public sealed class ByteReader(ReadOnlyMemory<byte> data)
{
    private readonly ReadOnlyMemory<byte> _data = data;

    public int Position { get; private set; }
    public int Remaining => _data.Length - Position;
    public bool IsAtEnd => Remaining == 0;

    public byte ReadByte() => Take(1)[0];
    public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
    public uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
    public int ReadInt32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
    public ulong ReadUInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));
    public long ReadInt64() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));

    /// <summary>32 bytes in wire order: a txid, block hash or merkle root.</summary>
    public UInt256 ReadUInt256() => new(Take(UInt256.Size));

    /// <summary>Copies the next <paramref name="count"/> bytes exactly as they are on the wire (no reversal).</summary>
    public byte[] ReadBytes(int count) => Take(count).ToArray();

    /// <summary>
    /// Mirrors Core's ReadCompactSize: rejects any value that has a shorter encoding, and, when
    /// <paramref name="rangeCheck"/> is on, any value above <see cref="CompactSize.MaxSize"/>.
    /// Counts and lengths always use the range check; Core skips it only for a few non-length fields.
    /// </summary>
    public ulong ReadCompactSize(bool rangeCheck = true)
    {
        var prefix = ReadByte();
        ulong value;
        if (prefix < 0xFD)
        {
            value = prefix;
        }
        else if (prefix == 0xFD)
        {
            value = ReadUInt16();
            if (value < 0xFD) throw new DecodeException(DecodeReasons.NonCanonicalCompactSize);
        }
        else if (prefix == 0xFE)
        {
            value = ReadUInt32();
            if (value < 0x1_0000) throw new DecodeException(DecodeReasons.NonCanonicalCompactSize);
        }
        else
        {
            value = ReadUInt64();
            if (value < 0x1_0000_0000) throw new DecodeException(DecodeReasons.NonCanonicalCompactSize);
        }

        if (rangeCheck && value > CompactSize.MaxSize)
            throw new DecodeException(DecodeReasons.CompactSizeTooLarge);
        return value;
    }

    /// <summary>A range-checked CompactSize used as an element count or byte length.</summary>
    public int ReadCount() => (int)ReadCompactSize(); // ≤ MaxSize, so it always fits in an int

    /// <summary>A CompactSize length followed by that many bytes (scripts, witness items).</summary>
    public byte[] ReadVarBytes() => ReadBytes(ReadCount());

    /// <summary>
    /// A CompactSize count followed by that many elements. A count may claim up to 32 MiB of
    /// elements. Like Core, which reads vectors in chunks, we never reserve more than the remaining
    /// bytes could hold, so a lying count ends in "end of data" rather than a huge allocation.
    /// </summary>
    public List<T> ReadVector<T>(Func<ByteReader, T> readElement, int minElementSize)
    {
        var count = ReadCount();
        var items = new List<T>(Math.Min(count, Remaining / minElementSize));
        for (var i = 0; i < count; i++) items.Add(readElement(this));
        return items;
    }

    private ReadOnlySpan<byte> Take(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count > Remaining) throw new DecodeException(DecodeReasons.EndOfData);
        var span = _data.Span.Slice(Position, count);
        Position += count;
        return span;
    }
}
