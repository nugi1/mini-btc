using System.Numerics;
using MiniBtc.Domain.Hashing;

namespace MiniBtc.Domain.Addresses;

/// <summary>
/// Base58 and Base58Check, as Bitcoin Core v30.0 src/base58.cpp defines them. Base58 writes a big
/// number with 58 symbols (no 0, O, I or l, which look alike); each leading zero byte becomes a
/// leading '1'. Base58Check appends the first 4 bytes of HASH256(payload), so a typo fails the
/// checksum instead of paying someone else.
/// </summary>
public static class Base58
{
    private const string Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var zeroes = 0;
        while (zeroes < data.Length && data[zeroes] == 0) zeroes++;

        var number = new BigInteger(data, isUnsigned: true, isBigEndian: true);
        var digits = new List<char>();
        while (number > 0)
        {
            number = BigInteger.DivRem(number, 58, out var remainder);
            digits.Add(Alphabet[(int)remainder]);
        }
        digits.Reverse();
        return new string('1', zeroes) + new string(digits.ToArray());
    }

    /// <summary>
    /// Core's DecodeBase58: whitespace around the string is skipped, any other character outside
    /// the alphabet fails, and so does a result longer than <paramref name="maxLength"/> bytes.
    /// </summary>
    public static bool TryDecode(string text, int maxLength, out byte[] data)
    {
        data = [];
        if (text.Contains('\0')) return false;
        var body = text.Trim(' ', '\f', '\n', '\r', '\t', '\v'); // Core's IsSpace

        var zeroes = 0;
        while (zeroes < body.Length && body[zeroes] == '1') zeroes++;
        if (zeroes > maxLength) return false;

        var number = BigInteger.Zero;
        foreach (var c in body.AsSpan(zeroes))
        {
            var digit = Alphabet.IndexOf(c);
            if (digit < 0) return false;
            number = number * 58 + digit;
        }

        var payload = number.IsZero ? Array.Empty<byte>() : number.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (zeroes + payload.Length > maxLength) return false;
        data = [.. new byte[zeroes], .. payload];
        return true;
    }

    public static string EncodeCheck(ReadOnlySpan<byte> payload)
    {
        var checksum = Hashes.Hash256(payload).ToWireBytes().AsSpan(0, 4);
        return Encode([.. payload, .. checksum]);
    }

    /// <summary>Core's DecodeBase58Check: the 4-byte checksum must match; returns the payload without it.</summary>
    public static bool TryDecodeCheck(string text, int maxPayloadLength, out byte[] payload)
    {
        payload = [];
        if (!TryDecode(text, maxPayloadLength + 4, out var data) || data.Length < 4) return false;

        var body = data.AsSpan(0, data.Length - 4);
        if (!Hashes.Hash256(body).ToWireBytes().AsSpan(0, 4).SequenceEqual(data.AsSpan(data.Length - 4))) return false;
        payload = body.ToArray();
        return true;
    }
}
