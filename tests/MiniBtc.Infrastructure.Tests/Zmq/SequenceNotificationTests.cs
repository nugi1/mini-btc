using System.Text;
using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Hashing;
using MiniBtc.Infrastructure.Zmq;

namespace MiniBtc.Infrastructure.Tests.Zmq;

public class SequenceNotificationTests
{
    private static readonly byte[] Topic = Encoding.ASCII.GetBytes("sequence");
    private static readonly UInt256 Genesis = NetworkParams.Regtest.GenesisHash;

    /// <summary>The hash as Core puts it in the body: display order, i.e. the wire bytes reversed.</summary>
    private static byte[] DisplayOrder(UInt256 hash) => Convert.FromHexString(hash.ToString());

    [Fact]
    public void A_block_connect_carries_the_hash_in_display_order_and_no_mempool_sequence()
    {
        byte[] body = [.. DisplayOrder(Genesis), (byte)'C'];

        Assert.True(SequenceNotification.TryParse([Topic, body, [0x07, 0x00, 0x00, 0x00]], out var notification));

        Assert.Equal(new SequenceNotification(Genesis, SequenceEvent.BlockConnected, null, 7), notification);
    }

    [Fact]
    public void A_block_disconnect_is_D()
    {
        byte[] body = [.. DisplayOrder(Genesis), (byte)'D'];

        Assert.True(SequenceNotification.TryParse([Topic, body, new byte[4]], out var notification));

        Assert.Equal(SequenceEvent.BlockDisconnected, notification.Event);
    }

    [Fact]
    public void Mempool_events_end_with_an_8_byte_little_endian_mempool_sequence()
    {
        byte[] added = [.. DisplayOrder(Genesis), (byte)'A', 0x2a, 0x01, 0, 0, 0, 0, 0, 0];
        byte[] removed = [.. DisplayOrder(Genesis), (byte)'R', 0x2b, 0x01, 0, 0, 0, 0, 0, 0];

        Assert.True(SequenceNotification.TryParse([Topic, added, new byte[4]], out var add));
        Assert.True(SequenceNotification.TryParse([Topic, removed, new byte[4]], out var remove));

        Assert.Equal((Genesis, SequenceEvent.TransactionAdded, (ulong?)298), (add.Hash, add.Event, add.MempoolSequence));
        Assert.Equal((SequenceEvent.TransactionRemoved, (ulong?)299), (remove.Event, remove.MempoolSequence));
    }

    [Fact]
    public void Rejects_unknown_labels_wrong_lengths_and_other_topics()
    {
        byte[] connect = [.. DisplayOrder(Genesis), (byte)'C'];

        Assert.False(SequenceNotification.TryParse([Topic, [.. DisplayOrder(Genesis), (byte)'X'], new byte[4]], out _));
        Assert.False(SequenceNotification.TryParse([Topic, [.. connect, 0], new byte[4]], out _)); // C has no mempool sequence
        Assert.False(SequenceNotification.TryParse([Topic, [.. DisplayOrder(Genesis), (byte)'A'], new byte[4]], out _)); // A must have one
        Assert.False(SequenceNotification.TryParse([Encoding.ASCII.GetBytes("rawblock"), connect, new byte[4]], out _));
        Assert.False(SequenceNotification.TryParse([Topic, connect], out _));
    }
}
