using MiniBtc.Domain.Blocks;
using MiniBtc.Domain.Scripts;
using MiniBtc.Domain.Tests.TestData;
using MiniBtc.Domain.Transactions;
using Org.BouncyCastle.Math;

namespace MiniBtc.Domain.Tests.Scripts;

public class P2pkhVerifierTests
{
    /// <summary>
    /// The legacy P2PKH spend Core's wallet signed in the captured "mixed" block, and the output it
    /// spends, found in the "segwit-spend" block.
    /// </summary>
    private static (Transaction Tx, TxOut Spent) CoreWalletSpend()
    {
        var mixed = Block.Parse(RegtestFixtures.Load("mixed").Raw);
        var tx = mixed.Transactions.Skip(1).Single(t => !t.HasWitness);
        var funding = Block.Parse(RegtestFixtures.Load("segwit-spend").Raw).Transactions
            .Single(t => t.Txid == tx.Inputs[0].PrevOut.Txid);
        return (tx, funding.Outputs[(int)tx.Inputs[0].PrevOut.Index]);
    }

    /// <summary>bitcoinjs "Standard transaction (1:1)", signed by private key 1, spending P2PKH(HASH160(G)).</summary>
    private static (Transaction Tx, TxOut Spent) BitcoinjsSpend() =>
        (Transaction.Parse(Convert.FromHexString(LegacyTransactions.StandardHex)),
         new TxOut(100_000, TestSigner.KeyOne.P2pkhScript)); // legacy sighash covers the script, not the value

    [Fact]
    public void Core_wallets_signature_verifies()
    {
        var (tx, spent) = CoreWalletSpend();

        Assert.Equal(ScriptVerification.Verified, P2pkhVerifier.Verify(tx, 0, spent));
    }

    [Fact]
    public void Bitcoinjs_signature_verifies()
    {
        var (tx, spent) = BitcoinjsSpend();

        Assert.Equal(ScriptVerification.Verified, P2pkhVerifier.Verify(tx, 0, spent));
    }

    [Fact]
    public void Legacy_sighash_of_cores_spend_matches_the_python_reference()
    {
        var (tx, spent) = CoreWalletSpend();

        var digest = LegacySignatureHash.ComputeAll(tx, 0, spent.ScriptPubKey.Span);

        // Computed independently with a 40-line Python implementation before this code was written.
        Assert.Equal("fc858670084c7b03878be6f2f89b7abba202d3d8567c094e7a75bee1fd643189", Convert.ToHexStringLower(digest.ToWireBytes()));
    }

    [Fact]
    public void Changing_any_output_breaks_the_signature()
    {
        var (tx, spent) = CoreWalletSpend();
        var output = tx.Outputs[0];
        var tampered = new Transaction(tx.Version, tx.Inputs, [new TxOut(output.Value + 1, output.ScriptPubKey.Span)], tx.LockTime);

        Assert.Equal(ScriptVerification.Failed(ScriptErrors.EvalFalse), P2pkhVerifier.Verify(tampered, 0, spent));
    }

    [Fact]
    public void A_pubkey_that_does_not_hash_to_the_address_fails_equalverify()
    {
        var (tx, _) = CoreWalletSpend();
        var someoneElses = new TxOut(1, TestSigner.KeyOne.P2pkhScript);

        Assert.Equal(ScriptVerification.Failed(ScriptErrors.EqualVerify), P2pkhVerifier.Verify(tx, 0, someoneElses));
    }

    [Fact]
    public void A_signature_that_is_not_strict_der_fails_sig_der()
    {
        var (tx, spent) = CoreWalletSpend();
        ScriptParser.TryParsePushes(tx.Inputs[0].ScriptSig.Span, out var pushes);
        var signature = pushes[0];
        signature[0] = 0x31; // not a DER SEQUENCE
        var forged = TestSigner.WithScriptSig(tx, 0, [(byte)signature.Length, .. signature, (byte)pushes[1].Length, .. pushes[1]]);

        Assert.Equal(ScriptVerification.Failed(ScriptErrors.SigDer), P2pkhVerifier.Verify(forged, 0, spent));
    }

    [Fact]
    public void A_high_s_signature_is_still_consensus_valid()
    {
        // (r, s) and (r, n − s) are both valid ECDSA signatures. Requiring the low s is relay policy only.
        var (tx, spent) = CoreWalletSpend();
        ScriptParser.TryParsePushes(tx.Inputs[0].ScriptSig.Span, out var pushes);
        var (r, s) = DerSignature.Parse(pushes[0].AsSpan(0, pushes[0].Length - 1));
        var highS = Secp256k1.Domain.N.Subtract(new BigInteger(1, s));
        byte[] signature = [.. TestSigner.Der(new BigInteger(1, r), highS), LegacySignatureHash.SighashAll];
        var flipped = TestSigner.WithScriptSig(tx, 0, [(byte)signature.Length, .. signature, (byte)pushes[1].Length, .. pushes[1]]);

        Assert.NotEqual(tx.Txid, flipped.Txid);   // same payment, different txid: the malleability SegWit fixed
        Assert.Equal(ScriptVerification.Verified, P2pkhVerifier.Verify(flipped, 0, spent));
    }

    [Fact]
    public void An_empty_signature_makes_checksig_false()
    {
        var (tx, spent) = CoreWalletSpend();
        ScriptParser.TryParsePushes(tx.Inputs[0].ScriptSig.Span, out var pushes);
        var noSignature = TestSigner.WithScriptSig(tx, 0, [0x00, (byte)pushes[1].Length, .. pushes[1]]);

        Assert.Equal(ScriptVerification.Failed(ScriptErrors.EvalFalse), P2pkhVerifier.Verify(noSignature, 0, spent));
    }

    [Fact]
    public void A_witness_on_a_p2pkh_input_is_unexpected()
    {
        var (tx, spent) = CoreWalletSpend();
        var input = tx.Inputs[0];
        var withWitness = new Transaction(tx.Version,
            [new TxIn(input.PrevOut, input.ScriptSig.Span, input.Sequence, new Witness([[0x01]]))], tx.Outputs, tx.LockTime);

        Assert.Equal(ScriptVerification.Failed(ScriptErrors.WitnessUnexpected), P2pkhVerifier.Verify(withWitness, 0, spent));
    }

    [Fact]
    public void Other_sighash_types_are_not_verified()
    {
        var (tx, spent) = CoreWalletSpend();
        ScriptParser.TryParsePushes(tx.Inputs[0].ScriptSig.Span, out var pushes);
        var signature = pushes[0];
        signature[^1] = 0x03; // SIGHASH_SINGLE, still strict DER
        var single = TestSigner.WithScriptSig(tx, 0, [(byte)signature.Length, .. signature, (byte)pushes[1].Length, .. pushes[1]]);

        Assert.Equal(ScriptStatus.NotVerified, P2pkhVerifier.Verify(single, 0, spent).Status);
    }

    [Fact]
    public void A_segwit_output_is_not_verified()
    {
        var (tx, _) = CoreWalletSpend();
        var p2wpkh = new TxOut(1, [0x00, 0x14, .. new byte[20]]);

        var outcome = P2pkhVerifier.Verify(tx, 0, p2wpkh);

        Assert.Equal(ScriptStatus.NotVerified, outcome.Status);
        Assert.Contains("accepted on Core's word", outcome.Detail);
    }

    [Fact]
    public void A_scriptsig_that_is_not_two_pushes_is_not_verified()
    {
        var (tx, spent) = CoreWalletSpend();
        var withOpcode = TestSigner.WithScriptSig(tx, 0, [.. tx.Inputs[0].ScriptSig.ToArray(), 0x61]); // + OP_NOP

        Assert.Equal(ScriptStatus.NotVerified, P2pkhVerifier.Verify(withOpcode, 0, spent).Status);
    }

    [Fact]
    public void A_signature_from_the_test_signer_verifies_and_a_high_s_one_too()
    {
        var unsigned = new Transaction(2, [new TxIn(new OutPoint(default, 0), ReadOnlySpan<byte>.Empty)],
            [new TxOut(1_000, TestSigner.KeyOne.P2pkhScript)], 0);
        var spent = new TxOut(2_000, TestSigner.KeyOne.P2pkhScript);

        Assert.Equal(ScriptVerification.Verified, P2pkhVerifier.Verify(TestSigner.KeyOne.SignInput(unsigned, 0), 0, spent));
        Assert.Equal(ScriptVerification.Verified, P2pkhVerifier.Verify(TestSigner.KeyOne.SignInput(unsigned, 0, lowS: false), 0, spent));
    }
}
