using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Serialization;

namespace MiniBtc.Domain.Transactions;

/// <summary>
/// An immutable transaction. Two serializations exist (BIP144):
/// <code>
///   legacy:    version | vin | vout | locktime
///   extended:  version | 00 (marker) | 01 (flag) | vin | vout | witness per input | locktime
/// </code>
/// txid  = HASH256 of the legacy bytes, so signatures, which live in the witness, cannot change it.
/// wtxid = HASH256 of the extended bytes, or the txid when no input has a witness.
/// </summary>
public sealed class Transaction
{
    private const byte WitnessFlag = 0x01;

    /// <summary>Smallest possible transaction: version, two empty lists, locktime.</summary>
    internal const int MinSize = 4 + 1 + 1 + 4;

    private readonly TxIn[] _inputs;
    private readonly TxOut[] _outputs;

    public Transaction(uint version, IEnumerable<TxIn> inputs, IEnumerable<TxOut> outputs, uint lockTime)
    {
        Version = version;
        _inputs = [.. inputs];
        _outputs = [.. outputs];
        LockTime = lockTime;
        HasWitness = _inputs.Any(input => !input.Witness.IsEmpty);
        // Immutable, so both ids are computed once (Core: ComputeHash / ComputeWitnessHash).
        Txid = Hashes.Hash256(ToBytes(includeWitness: false));
        Wtxid = HasWitness ? Hashes.Hash256(ToBytes(includeWitness: true)) : Txid;
    }

    /// <summary>Unsigned since Core 28 (PR #29325); the 4 bytes on the wire are the same either way.</summary>
    public uint Version { get; }
    public IReadOnlyList<TxIn> Inputs => _inputs;
    public IReadOnlyList<TxOut> Outputs => _outputs;
    public uint LockTime { get; }

    /// <summary>True when any input has a non-empty witness stack.</summary>
    public bool HasWitness { get; }

    /// <summary>HASH256 of the bytes without witnesses. What outpoints and the merkle root use.</summary>
    public UInt256 Txid { get; }

    /// <summary>HASH256 of the bytes with witnesses. What the witness commitment uses (BIP141).</summary>
    public UInt256 Wtxid { get; }

    /// <summary>Core: CTransaction::IsCoinBase — exactly one input, and its prevout is null.</summary>
    public bool IsCoinbase => _inputs.Length == 1 && _inputs[0].PrevOut.IsNull;

    /// <summary>
    /// Reads one transaction, in either format, and stops. Mirrors Core's UnserializeTransaction
    /// with witnesses allowed, as for every transaction inside a block.
    /// </summary>
    public static Transaction ReadFrom(ByteReader reader)
    {
        var version = reader.ReadUInt32();
        byte flags = 0;

        // Read vin. If the 0x00 marker is there, it reads as an empty input list.
        var inputs = reader.ReadVector(TxIn.ReadFrom, TxIn.MinSize);
        List<TxOut> outputs;
        if (inputs.Count == 0)
        {
            // A marker or a truly empty vin: the next byte decides. 0 means no flags and,
            // as in Core, no vout is read; anything else is a flag byte and the real lists follow.
            flags = reader.ReadByte();
            if (flags != 0)
            {
                inputs = reader.ReadVector(TxIn.ReadFrom, TxIn.MinSize);
                outputs = reader.ReadVector(TxOut.ReadFrom, TxOut.MinSize);
            }
            else
            {
                outputs = [];
            }
        }
        else
        {
            outputs = reader.ReadVector(TxOut.ReadFrom, TxOut.MinSize);
        }

        if ((flags & WitnessFlag) != 0)
        {
            flags ^= WitnessFlag;
            for (var i = 0; i < inputs.Count; i++)
                inputs[i] = inputs[i].WithWitness(Witness.ReadFrom(reader));
            // The flag promises witness data; all-empty stacks would be a second encoding of a legacy tx.
            if (inputs.All(input => input.Witness.IsEmpty))
                throw new DecodeException(DecodeReasons.SuperfluousWitness);
        }

        if (flags != 0) throw new DecodeException(DecodeReasons.UnknownOptionalData);

        var lockTime = reader.ReadUInt32();
        return new Transaction(version, inputs, outputs, lockTime);
    }

    /// <summary>
    /// Decodes bytes that must hold exactly one transaction, as sendrawtransaction expects.
    /// Leftover bytes fail the way Core's DecodeHexTx does.
    /// </summary>
    public static Transaction Parse(ReadOnlyMemory<byte> bytes)
    {
        var reader = new ByteReader(bytes);
        var tx = ReadFrom(reader);
        if (!reader.IsAtEnd) throw new DecodeException(DecodeReasons.TxDecodeFailed);
        return tx;
    }

    /// <summary>
    /// Extended format when <paramref name="includeWitness"/> is set and an input has a witness
    /// (Core: SerializeTransaction); otherwise legacy. A tx without witnesses has one encoding only.
    /// </summary>
    public void WriteTo(ByteWriter writer, bool includeWitness = true)
    {
        var extended = includeWitness && HasWitness;

        writer.WriteUInt32(Version);
        if (extended)
        {
            writer.WriteCompactSize(0); // the marker: an "empty vin"
            writer.WriteByte(WitnessFlag);
        }
        writer.WriteCompactSize((ulong)_inputs.Length);
        foreach (var input in _inputs) input.WriteTo(writer);
        writer.WriteCompactSize((ulong)_outputs.Length);
        foreach (var output in _outputs) output.WriteTo(writer);
        if (extended)
        {
            foreach (var input in _inputs) input.Witness.WriteTo(writer);
        }
        writer.WriteUInt32(LockTime);
    }

    public byte[] ToBytes(bool includeWitness = true)
    {
        var writer = new ByteWriter();
        WriteTo(writer, includeWitness);
        return writer.ToArray();
    }
}
