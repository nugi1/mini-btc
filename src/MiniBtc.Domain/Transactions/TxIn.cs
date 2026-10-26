using MiniBtc.Domain.Serialization;

namespace MiniBtc.Domain.Transactions;

/// <summary>
/// One input: which coin it spends, the unlocking script, and nSequence.
/// The script is kept as opaque bytes. Deserializing never parses it, so a malformed script is
/// still a well-formed transaction, as in Core. Scripts are judged later, by the validation rules.
/// </summary>
public sealed class TxIn
{
    public const uint SequenceFinal = 0xFFFF_FFFF;

    /// <summary>Smallest possible input: outpoint + empty script (1-byte length) + sequence.</summary>
    internal const int MinSize = OutPoint.Size + 1 + 4;

    private readonly byte[] _scriptSig;

    public TxIn(OutPoint prevOut, ReadOnlySpan<byte> scriptSig, uint sequence = SequenceFinal)
    {
        PrevOut = prevOut;
        _scriptSig = scriptSig.ToArray(); // own copy: the txid must not change under us
        Sequence = sequence;
    }

    public OutPoint PrevOut { get; }
    public ReadOnlyMemory<byte> ScriptSig => _scriptSig;
    public uint Sequence { get; }

    public static TxIn ReadFrom(ByteReader reader)
    {
        var prevOut = OutPoint.ReadFrom(reader);
        var scriptSig = reader.ReadVarBytes();
        var sequence = reader.ReadUInt32();
        return new TxIn(prevOut, scriptSig, sequence);
    }

    public void WriteTo(ByteWriter writer)
    {
        PrevOut.WriteTo(writer);
        writer.WriteVarBytes(_scriptSig);
        writer.WriteUInt32(Sequence);
    }
}
