using MassTransit;
using MiniBtc.Application.Contracts;

namespace MiniBtc.Api.Hosting;

/// <summary>
/// Shows the event path end to end: aggregate → outbox row → RabbitMQ → this consumer (through the
/// inbox). The SignalR hub in E5 consumes the same contract.
/// </summary>
public sealed class BlockConnectedLogConsumer(ILogger<BlockConnectedLogConsumer> logger) : IConsumer<BlockConnectedV1>
{
    public Task Consume(ConsumeContext<BlockConnectedV1> context)
    {
        logger.LogInformation("RabbitMQ delivered BlockConnectedV1: height {Height}, {Hash}, {Count} tx",
            context.Message.Height, context.Message.BlockHash, context.Message.TransactionCount);
        return Task.CompletedTask;
    }
}
