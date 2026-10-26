using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Serialization;
using MiniBtc.Domain.Transactions;

namespace MiniBtc.Domain.Blocks;

/// <summary>
/// A header followed by its transactions, a value object (Core: CBlock):
/// <code>
///   header 80 | CompactSize tx count | each tx in extended (witness) format
/// </code>
/// This is the exact payload of getblock &lt;hash&gt; 0 and of ZMQ rawblock.
/// </summary>
public sealed class Block
{
    private readonly Transaction[] _transactions;

    public Block(BlockHeader header, IEnumerable<Transaction> transactions)
    {
        Header = header;
        _transactions = [.. transactions];
    }

    public BlockHeader Header { get; }
    public IReadOnlyList<Transaction> Transactions => _transactions;
    public UInt256 Hash => Header.Hash;

    /// <summary>Core: BlockMerkleRoot — the merkle root over the txids (not wtxids).</summary>
    public UInt256 ComputeMerkleRoot(out bool mutated) =>
        MerkleTree.ComputeRoot([.. _transactions.Select(tx => tx.Txid)], out mutated);

    public static Block ReadFrom(ByteReader reader)
    {
        var header = BlockHeader.ReadFrom(reader);
        var transactions = reader.ReadVector(Transaction.ReadFrom, Transaction.MinSize);
        return new Block(header, transactions);
    }

    /// <summary>
    /// Decodes bytes that must hold exactly one block. Stricter than Core's DecodeHexBlk, which
    /// ignores leftover bytes: ours always come straight from Core (RPC or ZMQ), so a leftover
    /// byte means our decoder misread the block, and it must fail loudly.
    /// </summary>
    public static Block Parse(ReadOnlyMemory<byte> bytes)
    {
        var reader = new ByteReader(bytes);
        var block = ReadFrom(reader);
        if (!reader.IsAtEnd) throw new DecodeException(DecodeReasons.BlockDecodeFailed);
        return block;
    }

    public void WriteTo(ByteWriter writer)
    {
        Header.WriteTo(writer);
        writer.WriteCompactSize((ulong)_transactions.Length);
        foreach (var tx in _transactions) tx.WriteTo(writer, includeWitness: true);
    }

    public byte[] ToBytes()
    {
        var writer = new ByteWriter();
        WriteTo(writer);
        return writer.ToArray();
    }
}
