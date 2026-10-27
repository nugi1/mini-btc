using MassTransit;
using Microsoft.AspNetCore.SignalR;
using MiniBtc.Application.Contracts;

namespace MiniBtc.Api.Realtime;

/// <summary>
/// Forwards the node's published block events to every connected explorer. It consumes the same
/// contracts any other service would (BlockConnectedV1, BlockDisconnectedV1), so what the explorer
/// shows is exactly what left the node through the outbox.
/// </summary>
public sealed class ChainHubRelay(IHubContext<ChainHub, IChainClient> hub)
    : IConsumer<BlockConnectedV1>, IConsumer<BlockDisconnectedV1>
{
    public Task Consume(ConsumeContext<BlockConnectedV1> context) =>
        hub.Clients.All.BlockConnected(new BlockEvent(context.Message.BlockHash, context.Message.Height, context.Message.TransactionCount));

    public Task Consume(ConsumeContext<BlockDisconnectedV1> context) =>
        hub.Clients.All.BlockDisconnected(new BlockEvent(context.Message.BlockHash, context.Message.Height, context.Message.TransactionCount));
}
