using System.Text;
using MiniBtc.Domain.Consensus;
using MiniBtc.Infrastructure.Zmq;
using Patterns.DDD.MassTransit;

namespace MiniBtc.Infrastructure.Tests.Zmq;

public class RawBlockNotificationTests
{
    private static readonly byte[] Topic = Encoding.ASCII.GetBytes("rawblock");
    private static readonly byte[] GenesisBlock = NetworkParams.Regtest.GenesisBlock.ToBytes();

    [Fact]
    public void Reads_the_block_hash_from_the_header_and_the_little_endian_sequence()
    {
        Assert.True(RawBlockNotification.TryParse([Topic, GenesisBlock, [0x2a, 0x01, 0x00, 0x00]], out var notification));

        Assert.Equal(NetworkParams.Regtest.GenesisHash, notification.BlockHash);
        Assert.Equal(298u, notification.Sequence); // 0x0000012a
    }

    [Fact]
    public void The_header_alone_identifies_the_block()
    {
        Assert.True(RawBlockNotification.TryParse([Topic, GenesisBlock[..80], new byte[4]], out var notification));

        Assert.Equal(NetworkParams.Regtest.GenesisHash, notification.BlockHash);
    }

    [Fact]
    public void Rejects_other_topics_frame_counts_and_sizes()
    {
        Assert.False(RawBlockNotification.TryParse([Encoding.ASCII.GetBytes("rawtx"), GenesisBlock, new byte[4]], out _));
        Assert.False(RawBlockNotification.TryParse([Topic, GenesisBlock], out _));
        Assert.False(RawBlockNotification.TryParse([Topic, GenesisBlock[..79], new byte[4]], out _));
        Assert.False(RawBlockNotification.TryParse([Topic, GenesisBlock, new byte[8]], out _));
    }

    [Fact]
    public void The_same_block_always_gets_the_same_message_id()
    {
        var hash = NetworkParams.Regtest.GenesisHash.ToString();

        Assert.Equal(MessageIds.FromKey($"block:{hash}"), MessageIds.FromKey($"block:{hash}"));
        Assert.NotEqual(MessageIds.FromKey($"block:{hash}"), MessageIds.FromKey($"tx:{hash}"));
    }
}
