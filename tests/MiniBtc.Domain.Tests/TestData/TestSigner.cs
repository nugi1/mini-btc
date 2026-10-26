using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Scripts;
using MiniBtc.Domain.Transactions;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Math;

namespace MiniBtc.Domain.Tests.TestData;

/// <summary>
/// Signs P2PKH spends for tests, the way a wallet does (E3 does this in Python). Nonces are
/// deterministic (RFC 6979), so every test run produces the same signatures.
/// </summary>
internal sealed class TestSigner(BigInteger privateKey)
{
    public static readonly TestSigner KeyOne = new(BigInteger.One);

    public byte[] PublicKey { get; } = Secp256k1.Domain.G.Multiply(privateKey).Normalize().GetEncoded(compressed: true);
    public byte[] P2pkhScript => StandardScripts.P2pkh(Hashes.Hash160(PublicKey));

    /// <summary>Signs input <paramref name="index"/> (spending a P2PKH output of this key) and returns the signed tx.</summary>
    public Transaction SignInput(Transaction tx, int index, bool lowS = true)
    {
        var digest = LegacySignatureHash.ComputeAll(tx, index, P2pkhScript);
        var signer = new ECDsaSigner(new HMacDsaKCalculator(new Sha256Digest()));
        signer.Init(true, new ECPrivateKeyParameters(privateKey, Secp256k1.Domain));
        var rs = signer.GenerateSignature(digest.ToWireBytes());
        var (r, s) = (rs[0], rs[1]);
        var half = Secp256k1.Domain.N.ShiftRight(1);
        if (lowS == s.CompareTo(half) > 0) s = Secp256k1.Domain.N.Subtract(s); // pick the requested half

        byte[] signature = [.. Der(r, s), LegacySignatureHash.SighashAll];
        byte[] scriptSig = [(byte)signature.Length, .. signature, (byte)PublicKey.Length, .. PublicKey];
        return WithScriptSig(tx, index, scriptSig);
    }

    public static Transaction WithScriptSig(Transaction tx, int index, byte[] scriptSig) => new(
        tx.Version,
        tx.Inputs.Select((input, i) => i == index ? new TxIn(input.PrevOut, scriptSig, input.Sequence, input.Witness) : input),
        tx.Outputs,
        tx.LockTime);

    /// <summary>DER for (r, s): BouncyCastle's ToByteArray is already the minimal signed big-endian INTEGER body.</summary>
    public static byte[] Der(BigInteger r, BigInteger s)
    {
        var rb = r.ToByteArray();
        var sb = s.ToByteArray();
        return [0x30, (byte)(4 + rb.Length + sb.Length), 0x02, (byte)rb.Length, .. rb, 0x02, (byte)sb.Length, .. sb];
    }
}
