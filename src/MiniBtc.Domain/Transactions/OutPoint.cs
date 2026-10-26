using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Serialization;

namespace MiniBtc.Domain.Transactions;

/// <summary>
/// A reference to one output of an earlier transaction: its txid plus the output's index.
/// 36 bytes on the wire. It is the key of the UTXO set and of the mempool's spent-outpoint index.
/// </summary>
public readonly record struct OutPoint(UInt256 Txid, uint Index)
{
    public const int Size = UInt256.Size + 4;

    /// <summary>The coinbase input's prevout: zero txid, index 0xFFFFFFFF (Core: COutPoint::IsNull).</summary>
    public static OutPoint Null => new(UInt256.Zero, uint.MaxValue);

    public bool IsNull => this == Null;

    public static OutPoint ReadFrom(ByteReader reader) => new(reader.ReadUInt256(), reader.ReadUInt32());

    public void WriteTo(ByteWriter writer)
    {
        writer.WriteUInt256(Txid);
        writer.WriteUInt32(Index);
    }

    /// <summary>txid:vout, the form bitcoin-cli and explorers use.</summary>
    public override string ToString() => $"{Txid}:{Index}";
}
