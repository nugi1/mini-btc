using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Transactions;

namespace MiniBtc.Domain.Scripts;

/// <summary>
/// Verifies the spend of a P2PKH output without a general script interpreter. A standard P2PKH
/// scriptSig is &lt;sig&gt; &lt;pubkey&gt;; running it and then the scriptPubKey does, step by step:
/// <code>
///   OP_DUP OP_HASH160 &lt;h&gt; OP_EQUALVERIFY   HASH160(pubkey) must equal h
///   OP_CHECKSIG                             strict DER, then ECDSA over the legacy sighash
/// </code>
/// and the script succeeds only if OP_CHECKSIG leaves true. This class performs exactly those steps,
/// with Core's error for each failure. Other script types, other scriptSig forms and sighash types
/// other than ALL are NotVerified, never guessed.
/// </summary>
public static class P2pkhVerifier
{
    public static ScriptVerification Verify(Transaction tx, int inputIndex, TxOut spentOutput)
    {
        var input = tx.Inputs[inputIndex];
        var scriptPubKey = spentOutput.ScriptPubKey.Span;

        if (!StandardScripts.TryGetP2pkhHash(scriptPubKey, out var pubKeyHash))
            return ScriptVerification.NotVerified("script type not verified, accepted on Core's word");

        if (!ScriptParser.TryParsePushes(input.ScriptSig.Span, out var pushes) || pushes.Count != 2
            || pushes.Any(push => push.Length > ScriptParser.MaxElementSize))
            return ScriptVerification.NotVerified("scriptSig is not <sig> <pubkey>, accepted on Core's word");

        var (signature, publicKey) = (pushes[0], pushes[1]);

        // OP_DUP OP_HASH160 <h> OP_EQUALVERIFY
        if (!Hashes.Hash160(publicKey).AsSpan().SequenceEqual(pubKeyHash))
            return ScriptVerification.Failed(ScriptErrors.EqualVerify);

        // OP_CHECKSIG. An empty signature is allowed in encoding and simply makes CHECKSIG false.
        if (signature.Length > 0 && !DerSignature.IsValidEncoding(signature))
            return ScriptVerification.Failed(ScriptErrors.SigDer);

        var valid = false;
        if (signature.Length > 0)
        {
            var hashType = signature[^1];
            if (hashType != LegacySignatureHash.SighashAll)
                return ScriptVerification.NotVerified($"sighash type 0x{hashType:x2} not verified, accepted on Core's word");

            var digest = LegacySignatureHash.ComputeAll(tx, inputIndex, scriptPubKey);
            var (r, s) = DerSignature.Parse(signature.AsSpan(0, signature.Length - 1));
            valid = Secp256k1.Verify(digest, r, s, publicKey);
        }

        if (!valid)
            return ScriptVerification.Failed(ScriptErrors.EvalFalse);

        // With SegWit active, a non-witness script must be spent with an empty witness (BIP141).
        if (!input.Witness.IsEmpty)
            return ScriptVerification.Failed(ScriptErrors.WitnessUnexpected);

        return ScriptVerification.Verified;
    }
}
