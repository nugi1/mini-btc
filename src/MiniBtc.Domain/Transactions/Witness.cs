using MiniBtc.Domain.Serialization;

namespace MiniBtc.Domain.Transactions;

/// <summary>
/// One input's witness stack (BIP141): a list of byte strings, e.g. &lt;signature&gt; &lt;pubkey&gt;
/// for P2WPKH. On the wire: CompactSize item count, then each item as CompactSize length + bytes.
/// It is part of the wtxid but never of the txid.
/// </summary>
public sealed class Witness
{
    private readonly ReadOnlyMemory<byte>[] _items;

    public static Witness Empty { get; } = new([]);

    public Witness(IEnumerable<byte[]> items) => _items = [.. items.Select(item => (ReadOnlyMemory<byte>)item.ToArray())];

    public IReadOnlyList<ReadOnlyMemory<byte>> Items => _items;

    /// <summary>Core: CScriptWitness::IsNull — an empty stack.</summary>
    public bool IsEmpty => _items.Length == 0;

    public static Witness ReadFrom(ByteReader reader) =>
        new(reader.ReadVector(r => r.ReadVarBytes(), minElementSize: 1));

    public void WriteTo(ByteWriter writer)
    {
        writer.WriteCompactSize((ulong)_items.Length);
        foreach (var item in _items) writer.WriteVarBytes(item.Span);
    }
}
