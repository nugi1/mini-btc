using System.Numerics;
using MiniBtc.Domain.Hashing;

namespace MiniBtc.Domain.Blocks;

/// <summary>
/// The header's 4-byte "bits" field: a 256-bit target packed like a tiny floating-point number.
/// <code>
///   bits = 0xEEMMMMMM   →   target = MMMMMM × 256^(EE − 3)
/// </code>
/// EE is the target's length in bytes and MMMMMM its top three bytes. Bit 0x00800000 is a sign
/// bit, inherited from OpenSSL's bignum format; a valid target never sets it. Ported line by line
/// from Core's arith_uint256::SetCompact / GetCompact, including their edge cases.
/// </summary>
public static class CompactTarget
{
    private static readonly BigInteger Mod256 = BigInteger.One << 256;

    /// <summary>
    /// Core: SetCompact. <paramref name="negative"/> and <paramref name="overflow"/> report encodings
    /// that DeriveTarget rejects; the returned value is then meaningless, as in Core.
    /// </summary>
    public static UInt256 Decode(uint bits, out bool negative, out bool overflow)
    {
        var size = (int)(bits >> 24);
        var word = bits & 0x007F_FFFF;
        BigInteger value;
        if (size <= 3)
        {
            word >>= 8 * (3 - size);
            value = word;
        }
        else
        {
            value = new BigInteger(word) << (8 * (size - 3));
        }

        negative = word != 0 && (bits & 0x0080_0000) != 0;
        overflow = word != 0 && (size > 34 || (word > 0xFF && size > 33) || (word > 0xFFFF && size > 32));

        // arith_uint256 is 256 bits wide: bits shifted past the top are lost. Only an overflow case can do this.
        return UInt256.FromBigInteger(value % Mod256);
    }

    /// <summary>Core: GetCompact. Always picks the encoding with the sign bit clear.</summary>
    public static uint Encode(UInt256 target, bool negative = false)
    {
        var value = target.ToBigInteger();
        var size = (int)((value.GetBitLength() + 7) / 8);
        uint compact = size <= 3
            ? (uint)(value << (8 * (3 - size)))
            : (uint)(value >> (8 * (size - 3)));

        // The mantissa's top bit would read as "negative": move it down a byte and grow the size.
        if ((compact & 0x0080_0000) != 0)
        {
            compact >>= 8;
            size++;
        }

        compact |= (uint)size << 24;
        if (negative && (compact & 0x007F_FFFF) != 0) compact |= 0x0080_0000;
        return compact;
    }
}
