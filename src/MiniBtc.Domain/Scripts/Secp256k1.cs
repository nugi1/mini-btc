using MiniBtc.Domain.Hashing;
using Org.BouncyCastle.Crypto.EC;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Math;

namespace MiniBtc.Domain.Scripts;

/// <summary>
/// ECDSA verification on Bitcoin's curve, secp256k1, through BouncyCastle: .NET's own ECDsa
/// reaches secp256k1 only on some operating systems (not macOS).
/// </summary>
public static class Secp256k1
{
    public static readonly ECDomainParameters Domain = CreateDomain();

    /// <summary>
    /// Core: CPubKey::Verify. The key must parse: 33 bytes with prefix 02/03 (compressed), or 65
    /// bytes with 04 (uncompressed) or 06/07 (hybrid). Then the signature (r, s) must verify over the
    /// digest. Either s or n − s verifies: requiring the low one (BIP62 low-S) is relay policy, not
    /// consensus, and Core normalizes before verifying.
    /// </summary>
    public static bool Verify(UInt256 digest, byte[] r, byte[] s, ReadOnlySpan<byte> publicKey)
    {
        var expectedLength = publicKey.Length == 0 ? 0 : publicKey[0] switch
        {
            0x02 or 0x03 => 33,
            0x04 or 0x06 or 0x07 => 65,
            _ => 0,
        };
        if (expectedLength == 0 || publicKey.Length != expectedLength) return false;

        ECPublicKeyParameters key;
        try
        {
            key = new ECPublicKeyParameters(Domain.Curve.DecodePoint(publicKey.ToArray()), Domain);
        }
        catch (ArgumentException)
        {
            return false; // not a point on the curve
        }

        var signer = new ECDsaSigner();
        signer.Init(false, key);
        // The digest is used as a big-endian number of its 32 bytes in hash order (wire order).
        return signer.VerifySignature(digest.ToWireBytes(), new BigInteger(1, r), new BigInteger(1, s));
    }

    private static ECDomainParameters CreateDomain()
    {
        var curve = CustomNamedCurves.GetByName("secp256k1");
        return new ECDomainParameters(curve.Curve, curve.G, curve.N, curve.H);
    }
}
