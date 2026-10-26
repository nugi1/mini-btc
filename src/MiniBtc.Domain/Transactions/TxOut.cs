using MiniBtc.Domain.Serialization;

namespace MiniBtc.Domain.Transactions;

/// <summary>
/// One output: an amount in satoshis and the locking script. Core reads nValue as a signed
/// int64 (CAmount) and accepts any value here. Range checks such as bad-txns-vout-negative are
/// consensus rules (E2), not decoding.
/// </summary>
public sealed class TxOut
{
    /// <summary>Smallest possible output: 8-byte value + empty script (1-byte length).</summary>
    internal const int MinSize = 8 + 1;

    private readonly byte[] _scriptPubKey;

    public TxOut(long value, ReadOnlySpan<byte> scriptPubKey)
    {
        Value = value;
        _scriptPubKey = scriptPubKey.ToArray();
    }

    public long Value { get; }
    public ReadOnlyMemory<byte> ScriptPubKey => _scriptPubKey;

    public static TxOut ReadFrom(ByteReader reader)
    {
        var value = reader.ReadInt64();
        var scriptPubKey = reader.ReadVarBytes();
        return new TxOut(value, scriptPubKey);
    }

    public void WriteTo(ByteWriter writer)
    {
        writer.WriteInt64(Value);
        writer.WriteVarBytes(_scriptPubKey);
    }
}
