using MassTransit;
using MiniBtc.Application.ChainSync;
using MiniBtc.Application.Contracts;

namespace MiniBtc.Api.Hosting;

/// <summary>
/// Receives Core's block announcements through the inbox (so a redelivered announcement is handled
/// once) and wakes the sync worker. It does not sync itself: one runner means no two syncs ever race,
/// and the worker commits block by block instead of inside this message's transaction.
/// </summary>
public sealed class BlockAnnouncedConsumer(ChainSyncSignal signal, ILogger<BlockAnnouncedConsumer> logger) : IConsumer<BlockAnnounced>
{
    public Task Consume(ConsumeContext<BlockAnnounced> context)
    {
        logger.LogInformation("Core announced block {Hash} (ZMQ #{Sequence}); syncing", context.Message.BlockHash, context.Message.ZmqSequence);
        signal.Request();
        return Task.CompletedTask;
    }
}
