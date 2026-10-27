using MassTransit;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MiniBtc.Application.Contracts;
using NetMQ;
using NetMQ.Sockets;
using Patterns.DDD.MassTransit;

namespace MiniBtc.Infrastructure.Zmq;

/// <summary>
/// Listens to Core's sequence topic and turns each event into a bus message: block connects and
/// disconnects become BlockAnnounced (a sync trigger, as for rawblock), mempool adds and removes
/// become MempoolTxAdded / MempoolTxRemoved. Message ids come from the event's identity, so the
/// consumers' inbox drops redeliveries. A connect has the same id as rawblock's announcement of the
/// same block, so whichever arrives second is dropped too.
/// </summary>
public sealed class ZmqSequenceSubscriber(IBus bus, ZmqOptions options, ILogger<ZmqSequenceSubscriber> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Factory.StartNew(() => ListenAsync(stoppingToken), stoppingToken, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

    private async Task ListenAsync(CancellationToken stoppingToken)
    {
        using var socket = new SubscriberSocket();
        socket.Connect(options.SequenceEndpoint);
        socket.Subscribe(SequenceNotification.Topic);
        logger.LogInformation("Listening for Core's ZMQ {Topic} on {Endpoint}", SequenceNotification.Topic, options.SequenceEndpoint);

        uint? lastSequence = null;
        NetMQMessage? message = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!socket.TryReceiveMultipartMessage(TimeSpan.FromSeconds(1), ref message, expectedFrameCount: 3))
                continue;

            var frames = message.Select(frame => frame.ToByteArray()).ToList();
            if (!SequenceNotification.TryParse(frames, out var notification))
            {
                logger.LogWarning("Ignoring a malformed ZMQ sequence message with {Count} frame(s)", frames.Count);
                continue;
            }

            if (lastSequence is { } last && notification.Sequence != last + 1)
                logger.LogWarning("ZMQ sequence jumped from {Last} to {Current}: events were lost; the next sync and mempool reload cover them",
                    last, notification.Sequence);
            lastSequence = notification.Sequence;

            await PublishAsync(notification, stoppingToken);
        }
    }

    private Task PublishAsync(SequenceNotification notification, CancellationToken cancellationToken)
    {
        var hash = notification.Hash.ToString();
        var mempoolSequence = notification.MempoolSequence ?? 0;
        return notification.Event switch
        {
            SequenceEvent.BlockConnected => bus.Publish(
                new BlockAnnounced(hash, notification.Sequence),
                context => context.MessageId = MessageIds.FromKey($"block:{hash}"), cancellationToken),
            SequenceEvent.BlockDisconnected => bus.Publish(
                new BlockAnnounced(hash, notification.Sequence),
                context => context.MessageId = MessageIds.FromKey($"block:{hash}:disconnected"), cancellationToken),
            SequenceEvent.TransactionAdded => bus.Publish(
                new MempoolTxAdded(hash, mempoolSequence),
                context => context.MessageId = MessageIds.FromKey($"tx:{hash}:added:{mempoolSequence}"), cancellationToken),
            _ => bus.Publish(
                new MempoolTxRemoved(hash, mempoolSequence),
                context => context.MessageId = MessageIds.FromKey($"tx:{hash}:removed:{mempoolSequence}"), cancellationToken),
        };
    }
}
