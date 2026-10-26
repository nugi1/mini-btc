using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Serialization;

namespace MiniBtc.Domain.Transactions;

/// <summary>
/// An immutable transaction in the legacy (pre-SegWit) serialization:
/// <code>
///   version   uint32 LE
///   vin       CompactSize count, then each TxIn
///   vout      CompactSize count, then each TxOut
///   locktime  uint32 LE
/// </code>
/// txid = HASH256 of exactly these bytes. The witness format arrives in the next commit.
/// </summary>
public sealed class Transaction
{
    private readonly TxIn[] _inputs;
    private readonly TxOut[] _outputs;

    public Transaction(uint version, IEnumerable<TxIn> inputs, IEnumerable<TxOut> outputs, uint lockTime)
    {
        Version = version;
        _inputs = [.. inputs];
        _outputs = [.. outputs];
        LockTime = lockTime;
        Txid = Hashes.Hash256(ToBytes()); // immutable, so computed once
    }

    /// <summary>Unsigned since Core 28 (PR #29325); the 4 bytes on the wire are the same either way.</summary>
    public uint Version { get; }
    public IReadOnlyList<TxIn> Inputs => _inputs;
    public IReadOnlyList<TxOut> Outputs => _outputs;
    public uint LockTime { get; }
    public UInt256 Txid { get; }

    /// <summary>Core: CTransaction::IsCoinBase — exactly one input, and its prevout is null.</summary>
    public bool IsCoinbase => _inputs.Length == 1 && _inputs[0].PrevOut.IsNull;

    /// <summary>Reads one transaction and stops; used for the transactions inside a block.</summary>
    public static Transaction ReadFrom(ByteReader reader)
    {
        var version = reader.ReadUInt32();
        var inputs = ReadVector(reader, TxIn.MinSize, TxIn.ReadFrom);
        var outputs = ReadVector(reader, TxOut.MinSize, TxOut.ReadFrom);
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

    public void WriteTo(ByteWriter writer)
    {
        writer.WriteUInt32(Version);
        writer.WriteCompactSize((ulong)_inputs.Length);
        foreach (var input in _inputs) input.WriteTo(writer);
        writer.WriteCompactSize((ulong)_outputs.Length);
        foreach (var output in _outputs) output.WriteTo(writer);
        writer.WriteUInt32(LockTime);
    }

    public byte[] ToBytes()
    {
        var writer = new ByteWriter();
        WriteTo(writer);
        return writer.ToArray();
    }

    // A count may claim up to 32 MiB of elements. Like Core, which reads vectors in chunks,
    // we never reserve more than the remaining bytes could hold; a lying count hits end of data.
    private static List<T> ReadVector<T>(ByteReader reader, int minElementSize, Func<ByteReader, T> read)
    {
        var count = reader.ReadCount();
        var items = new List<T>(Math.Min(count, reader.Remaining / minElementSize));
        for (var i = 0; i < count; i++) items.Add(read(reader));
        return items;
    }
}
