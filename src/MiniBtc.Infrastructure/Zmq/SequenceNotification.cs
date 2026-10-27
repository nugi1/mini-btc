using System.Buffers.Binary;
using System.Text;
using MiniBtc.Domain.Hashing;

namespace MiniBtc.Infrastructure.Zmq;

/// <summary>What a ZMQ sequence message reports (the label byte in its body).</summary>
public enum SequenceEvent : byte
{
    BlockConnected = (byte)'C',
    BlockDisconnected = (byte)'D',
    TransactionAdded = (byte)'A',
    TransactionRemoved = (byte)'R',
}

/// <summary>
/// One ZMQ "sequence" notification from Bitcoin Core (doc/zmq.md). Three frames: the topic
/// "sequence"; a body of a 32-byte hash in display order (reversed from the wire), a label byte, and
/// for A and R an 8-byte little-endian mempool sequence number; then a 4-byte little-endian message
/// sequence number counted per topic. Unlike rawblock, it reports disconnects and mempool changes.
/// </summary>
public readonly record struct SequenceNotification(UInt256 Hash, SequenceEvent Event, ulong? MempoolSequence, uint Sequence)
{
    public const string Topic = "sequence";

    public static bool TryParse(IReadOnlyList<byte[]> frames, out SequenceNotification notification)
    {
        notification = default;
        if (frames.Count != 3 || Encoding.ASCII.GetString(frames[0]) != Topic || frames[2].Length != 4) return false;

        var body = frames[1];
        if (body.Length < 33) return false;
        var label = (SequenceEvent)body[32];
        var expectedLength = label switch
        {
            SequenceEvent.BlockConnected or SequenceEvent.BlockDisconnected => 33,
            SequenceEvent.TransactionAdded or SequenceEvent.TransactionRemoved => 41,
            _ => -1,
        };
        if (body.Length != expectedLength) return false;

        var wireOrder = body.AsSpan(0, 32).ToArray();
        Array.Reverse(wireOrder); // Core sends the hash as displayed; UInt256 takes wire order
        notification = new SequenceNotification(
            new UInt256(wireOrder),
            label,
            expectedLength == 41 ? BinaryPrimitives.ReadUInt64LittleEndian(body.AsSpan(33, 8)) : null,
            BinaryPrimitives.ReadUInt32LittleEndian(frames[2]));
        return true;
    }
}
