using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Digests;

namespace MiniBtc.Domain.Hashing;

/// <summary>Bitcoin's two composite hashes (Core: Hash / Hash160 in hash.h).</summary>
public static class Hashes
{
    /// <summary>
    /// HASH256 = SHA-256(SHA-256(data)). Gives txids, wtxids, block hashes, merkle nodes,
    /// the Base58Check checksum and the legacy signature hash.
    /// </summary>
    public static UInt256 Hash256(ReadOnlySpan<byte> data)
    {
        Span<byte> first = stackalloc byte[32];
        Span<byte> second = stackalloc byte[32];
        SHA256.HashData(data, first);
        SHA256.HashData(first, second);
        return new UInt256(second);
    }

    /// <summary>
    /// HASH160 = RIPEMD-160(SHA-256(data)), 20 bytes. Turns a public key (P2PKH) or a redeem
    /// script (P2SH) into the hash inside the address. Never displayed reversed.
    /// </summary>
    public static byte[] Hash160(ReadOnlySpan<byte> data) => Ripemd160(SHA256.HashData(data));

    /// <summary>
    /// RIPEMD-160 from BouncyCastle: .NET only ships it on .NET Framework, not on .NET Core/5+.
    /// </summary>
    public static byte[] Ripemd160(ReadOnlySpan<byte> data)
    {
        var digest = new RipeMD160Digest();
        var input = data.ToArray();
        digest.BlockUpdate(input, 0, input.Length);
        var result = new byte[digest.GetDigestSize()];
        digest.DoFinal(result, 0);
        return result;
    }
}
