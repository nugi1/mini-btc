using MassTransit;
using Microsoft.AspNetCore.SignalR;
using MiniBtc.Api.Realtime;
using MiniBtc.Application.Contracts;
using MiniBtc.Application.Unconfirmed;
using MiniBtc.Domain.Hashing;

namespace MiniBtc.Api.Hosting;

/// <summary>
/// Applies Core's mempool adds and removes to the node's mempool view. Both message types share this
/// one consumer, so they share one queue, and the definition below handles one message at a time:
/// an add and a remove of the same transaction are applied in the order Core sent them.
/// </summary>
public sealed class MempoolEventsConsumer(
    MempoolSynchronizer mempool, IHubContext<ChainHub, IChainClient> hub, ILogger<MempoolEventsConsumer> logger)
    : IConsumer<MempoolTxAdded>, IConsumer<MempoolTxRemoved>
{
    public async Task Consume(ConsumeContext<MempoolTxAdded> context)
    {
        var message = context.Message;
        if (await mempool.AddedAsync(UInt256.Parse(message.Txid), message.MempoolSequence, context.CancellationToken))
        {
            logger.LogInformation("Mempool +{Txid} (mempool #{Sequence})", message.Txid, message.MempoolSequence);
            await hub.Clients.All.TxAccepted(new TxAcceptedEvent(message.Txid, "core"));
        }
    }

    public async Task Consume(ConsumeContext<MempoolTxRemoved> context)
    {
        var message = context.Message;
        if (mempool.Removed(UInt256.Parse(message.Txid), message.MempoolSequence))
        {
            logger.LogInformation("Mempool -{Txid} (mempool #{Sequence})", message.Txid, message.MempoolSequence);
            await hub.Clients.All.TxRemoved(new TxRemovedEvent(message.Txid));
        }
    }
}

public sealed class MempoolEventsConsumerDefinition : ConsumerDefinition<MempoolEventsConsumer>
{
    public MempoolEventsConsumerDefinition() => ConcurrentMessageLimit = 1;
}
