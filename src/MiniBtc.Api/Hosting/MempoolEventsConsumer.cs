using MassTransit;
using MiniBtc.Application.Contracts;
using MiniBtc.Application.Unconfirmed;
using MiniBtc.Domain.Hashing;

namespace MiniBtc.Api.Hosting;

/// <summary>
/// Applies Core's mempool adds and removes to the node's mempool view. Both message types share this
/// one consumer, so they share one queue, and the definition below handles one message at a time:
/// an add and a remove of the same transaction are applied in the order Core sent them.
/// </summary>
public sealed class MempoolEventsConsumer(MempoolSynchronizer mempool, ILogger<MempoolEventsConsumer> logger)
    : IConsumer<MempoolTxAdded>, IConsumer<MempoolTxRemoved>
{
    public async Task Consume(ConsumeContext<MempoolTxAdded> context)
    {
        var message = context.Message;
        if (await mempool.AddedAsync(UInt256.Parse(message.Txid), message.MempoolSequence, context.CancellationToken))
            logger.LogInformation("Mempool +{Txid} (mempool #{Sequence})", message.Txid, message.MempoolSequence);
    }

    public Task Consume(ConsumeContext<MempoolTxRemoved> context)
    {
        var message = context.Message;
        if (mempool.Removed(UInt256.Parse(message.Txid), message.MempoolSequence))
            logger.LogInformation("Mempool -{Txid} (mempool #{Sequence})", message.Txid, message.MempoolSequence);
        return Task.CompletedTask;
    }
}

public sealed class MempoolEventsConsumerDefinition : ConsumerDefinition<MempoolEventsConsumer>
{
    public MempoolEventsConsumerDefinition() => ConcurrentMessageLimit = 1;
}
