namespace MiniBtc.Domain.Scripts;

/// <summary>Recognizes the output script templates the node understands.</summary>
public static class StandardScripts
{
    private const byte OpDup = 0x76;
    private const byte OpHash160 = 0xa9;
    private const byte OpEqualVerify = 0x88;
    private const byte OpCheckSig = 0xac;

    /// <summary>
    /// P2PKH: OP_DUP OP_HASH160 &lt;20-byte pubkey hash&gt; OP_EQUALVERIFY OP_CHECKSIG, 25 bytes.
    /// Spending it takes a public key that hashes to the 20 bytes, and a signature by that key.
    /// </summary>
    public static bool TryGetP2pkhHash(ReadOnlySpan<byte> scriptPubKey, out byte[] pubKeyHash)
    {
        if (scriptPubKey.Length == 25 && scriptPubKey[0] == OpDup && scriptPubKey[1] == OpHash160 && scriptPubKey[2] == 20
            && scriptPubKey[23] == OpEqualVerify && scriptPubKey[24] == OpCheckSig)
        {
            pubKeyHash = scriptPubKey[3..23].ToArray();
            return true;
        }
        pubKeyHash = [];
        return false;
    }

    public static byte[] P2pkh(ReadOnlySpan<byte> pubKeyHash)
    {
        if (pubKeyHash.Length != 20) throw new ArgumentException("A pubkey hash is 20 bytes.", nameof(pubKeyHash));
        return [OpDup, OpHash160, 20, .. pubKeyHash, OpEqualVerify, OpCheckSig];
    }
}
