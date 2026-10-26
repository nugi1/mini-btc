namespace MiniBtc.Domain.Serialization;

/// <summary>
/// Bitcoin's variable-length unsigned integer, used for every count and length on the wire.
/// <code>
///   value &lt; 0xFD          1 byte   value itself
///   value ≤ 0xFFFF         3 bytes  0xFD + uint16 LE
///   value ≤ 0xFFFF_FFFF    5 bytes  0xFE + uint32 LE
///   otherwise              9 bytes  0xFF + uint64 LE
/// </code>
/// Only the shortest encoding is valid (see <see cref="ByteReader.ReadCompactSize"/>).
/// </summary>
public static class CompactSize
{
    /// <summary>serialize.h MAX_SIZE: no length or count read from the wire may exceed 32 MiB.</summary>
    public const ulong MaxSize = 0x0200_0000;

    /// <summary>Encoded length in bytes (Core: GetSizeOfCompactSize).</summary>
    public static int SizeOf(ulong value) => value switch
    {
        < 0xFD => 1,
        <= 0xFFFF => 3,
        <= 0xFFFF_FFFF => 5,
        _ => 9,
    };
}
