using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Serialization;
using MiniBtc.Domain.Transactions;

namespace MiniBtc.Domain.Scripts;

/// <summary>
/// The message a pre-SegWit signature commits to (Core: SignatureHash with SigVersion::BASE).
/// A signature can't cover itself, so the signer hashes a modified copy of the transaction.
/// </summary>
public static class LegacySignatureHash
{
    /// <summary>Sign every input and every output: the usual case.</summary>
    public const byte SighashAll = 0x01;

    /// <summary>
    /// SIGHASH_ALL for input <paramref name="inputIndex"/> (Core: CTransactionSignatureSerializer):
    /// <code>
    ///   version | inputs: every scriptSig emptied, except the signed input's, which becomes
    ///   the spent output's script (scriptCode) | all outputs | locktime | 01 00 00 00
    /// </code>
    /// then HASH256. No witness is ever included. P2PKH scripts contain no OP_CODESEPARATOR, so the
    /// scriptCode is the scriptPubKey unchanged.
    /// </summary>
    public static UInt256 ComputeAll(Transaction tx, int inputIndex, ReadOnlySpan<byte> scriptCode)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(inputIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(inputIndex, tx.Inputs.Count);

        var writer = new ByteWriter();
        writer.WriteUInt32(tx.Version);
        writer.WriteCompactSize((ulong)tx.Inputs.Count);
        for (var i = 0; i < tx.Inputs.Count; i++)
        {
            var input = tx.Inputs[i];
            input.PrevOut.WriteTo(writer);
            if (i == inputIndex) writer.WriteVarBytes(scriptCode);
            else writer.WriteCompactSize(0);
            writer.WriteUInt32(input.Sequence);
        }
        writer.WriteCompactSize((ulong)tx.Outputs.Count);
        foreach (var output in tx.Outputs) output.WriteTo(writer);
        writer.WriteUInt32(tx.LockTime);
        writer.WriteUInt32(SighashAll); // the hash type, as 4 bytes
        return Hashes.Hash256(writer.WrittenSpan);
    }
}
