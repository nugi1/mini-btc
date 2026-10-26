using System.Buffers;
using System.Buffers.Binary;
using MiniBtc.Domain.Hashing;

namespace MiniBtc.Domain.Serialization;

/// <summary>Append-only writer for Bitcoin's wire format; the exact inverse of <see cref="ByteReader"/>.</summary>
public sealed class ByteWriter(int initialCapacity = 256)
{
    private readonly ArrayBufferWriter<byte> _buffer = new(initialCapacity);

    public int Length => _buffer.WrittenCount;
    public ReadOnlySpan<byte> WrittenSpan => _buffer.WrittenSpan;
    public byte[] ToArray() => _buffer.WrittenSpan.ToArray();

    public void WriteByte(byte value)
    {
        _buffer.GetSpan(1)[0] = value;
        _buffer.Advance(1);
    }

    public void WriteUInt16(ushort value) => Write(2, value, BinaryPrimitives.WriteUInt16LittleEndian);
    public void WriteUInt32(uint value) => Write(4, value, BinaryPrimitives.WriteUInt32LittleEndian);
    public void WriteInt32(int value) => Write(4, value, BinaryPrimitives.WriteInt32LittleEndian);
    public void WriteUInt64(ulong value) => Write(8, value, BinaryPrimitives.WriteUInt64LittleEndian);
    public void WriteInt64(long value) => Write(8, value, BinaryPrimitives.WriteInt64LittleEndian);

    /// <summary>32 bytes in wire order.</summary>
    public void WriteUInt256(UInt256 value)
    {
        value.WriteTo(_buffer.GetSpan(UInt256.Size));
        _buffer.Advance(UInt256.Size);
    }

    /// <summary>Appends bytes as they are (no reversal).</summary>
    public void WriteBytes(ReadOnlySpan<byte> bytes) => _buffer.Write(bytes);

    /// <summary>Always the shortest encoding, as Core's WriteCompactSize does.</summary>
    public void WriteCompactSize(ulong value)
    {
        if (value < 0xFD)
        {
            WriteByte((byte)value);
        }
        else if (value <= 0xFFFF)
        {
            WriteByte(0xFD);
            WriteUInt16((ushort)value);
        }
        else if (value <= 0xFFFF_FFFF)
        {
            WriteByte(0xFE);
            WriteUInt32((uint)value);
        }
        else
        {
            WriteByte(0xFF);
            WriteUInt64(value);
        }
    }

    /// <summary>CompactSize length, then the bytes.</summary>
    public void WriteVarBytes(ReadOnlySpan<byte> bytes)
    {
        WriteCompactSize((ulong)bytes.Length);
        WriteBytes(bytes);
    }

    private delegate void SpanWriter<in T>(Span<byte> destination, T value);

    private void Write<T>(int size, T value, SpanWriter<T> write)
    {
        write(_buffer.GetSpan(size), value);
        _buffer.Advance(size);
    }
}
