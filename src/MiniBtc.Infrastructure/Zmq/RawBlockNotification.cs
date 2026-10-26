using System.Buffers.Binary;
using System.Text;
using MiniBtc.Domain.Blocks;
using MiniBtc.Domain.Hashing;

namespace MiniBtc.Infrastructure.Zmq;

/// <summary>
/// One ZMQ rawblock notification from Bitcoin Core (doc/zmq.md): three frames, topic "rawblock", the
/// serialized block, and a 4-byte little-endian sequence number counted per topic.
/// </summary>
public readonly record struct RawBlockNotification(UInt256 BlockHash, uint Sequence)
{
    public const string Topic = "rawblock";

    /// <summary>
    /// The block hash is HASH256 of the first 80 bytes, so the header alone identifies the block;
    /// the rest is left for ChainSync, which fetches and validates the block itself.
    /// </summary>
    public static bool TryParse(IReadOnlyList<byte[]> frames, out RawBlockNotification notification)
    {
        notification = default;
        if (frames.Count != 3 || Encoding.ASCII.GetString(frames[0]) != Topic) return false;
        if (frames[1].Length < BlockHeader.Size || frames[2].Length != 4) return false;

        notification = new RawBlockNotification(
            Hashes.Hash256(frames[1].AsSpan(0, BlockHeader.Size)),
            BinaryPrimitives.ReadUInt32LittleEndian(frames[2]));
        return true;
    }
}
