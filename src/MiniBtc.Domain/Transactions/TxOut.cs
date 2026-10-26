using MiniBtc.Domain.Serialization;

namespace MiniBtc.Domain.Transactions;

/// <summary>
/// One output: an amount in satoshis and the locking script. Core reads nValue as a signed
/// int64 (CAmount) and accepts any value here. Range checks such as bad-txns-vout-negative are
/// consensus rules (E2), not decoding. A value object: equal when value and script are equal.
/// </summary>
public sealed class TxOut : IEquatable<TxOut>
{
    /// <summary>Smallest possible output: 8-byte value + empty script (1-byte length).</summary>
    internal const int MinSize = 8 + 1;

    /// <summary>script/script.h MAX_SCRIPT_SIZE: a longer script can never be executed.</summary>
    public const int MaxScriptSize = 10_000;

    private const byte OpReturn = 0x6a;

    private readonly byte[] _scriptPubKey;

    public TxOut(long value, ReadOnlySpan<byte> scriptPubKey)
    {
        Value = value;
        _scriptPubKey = scriptPubKey.ToArray();
    }

    public long Value { get; }
    public ReadOnlyMemory<byte> ScriptPubKey => _scriptPubKey;

    /// <summary>
    /// Core: CScript::IsUnspendable — starts with OP_RETURN, or is longer than MAX_SCRIPT_SIZE.
    /// No input can ever satisfy such a script, so Core never adds it to the UTXO set (AddCoin).
    /// The coinbase's witness commitment (OP_RETURN aa21a9ed…) is one.
    /// </summary>
    public bool IsUnspendable => (_scriptPubKey.Length > 0 && _scriptPubKey[0] == OpReturn) || _scriptPubKey.Length > MaxScriptSize;

    public bool Equals(TxOut? other) =>
        other is not null && Value == other.Value && _scriptPubKey.AsSpan().SequenceEqual(other._scriptPubKey);

    public override bool Equals(object? obj) => Equals(obj as TxOut);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Value);
        hash.AddBytes(_scriptPubKey);
        return hash.ToHashCode();
    }

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
