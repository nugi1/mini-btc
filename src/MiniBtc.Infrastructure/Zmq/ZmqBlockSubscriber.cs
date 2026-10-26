using MassTransit;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MiniBtc.Application.Contracts;
using NetMQ;
using NetMQ.Sockets;
using Patterns.DDD.MassTransit;

namespace MiniBtc.Infrastructure.Zmq;

public sealed class ZmqOptions
{
    public const string SectionName = "Zmq";

    /// <summary>Core's -zmqpubrawblock address, as published by docker-compose.yml.</summary>
    public string RawBlockEndpoint { get; set; } = "tcp://127.0.0.1:28332";
}

/// <summary>
/// Listens to Core's rawblock topic and publishes each notification to the bus as BlockAnnounced,
/// with a message id derived from the block hash, so the consumer's inbox deduplicates. It publishes
/// through IBus, not the outbox: an external notification changes no state of ours to commit with.
/// </summary>
public sealed class ZmqBlockSubscriber(IBus bus, ZmqOptions options, ILogger<ZmqBlockSubscriber> logger) : BackgroundService
{
    // NetMQ sockets block; run the loop on its own thread and poll for cancellation once a second.
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Factory.StartNew(() => ListenAsync(stoppingToken), stoppingToken, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

    private async Task ListenAsync(CancellationToken stoppingToken)
    {
        using var socket = new SubscriberSocket();
        socket.Connect(options.RawBlockEndpoint);
        socket.Subscribe(RawBlockNotification.Topic);
        logger.LogInformation("Listening for Core's ZMQ {Topic} on {Endpoint}", RawBlockNotification.Topic, options.RawBlockEndpoint);

        uint? lastSequence = null;
        NetMQMessage? message = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!socket.TryReceiveMultipartMessage(TimeSpan.FromSeconds(1), ref message, expectedFrameCount: 3))
                continue;

            var frames = message.Select(frame => frame.ToByteArray()).ToList();
            if (!RawBlockNotification.TryParse(frames, out var notification))
            {
                logger.LogWarning("Ignoring a malformed ZMQ message with {Count} frame(s)", frames.Count);
                continue;
            }

            if (lastSequence is { } last && notification.Sequence != last + 1)
                logger.LogWarning("ZMQ sequence jumped from {Last} to {Current}: notifications were lost; the next sync covers them",
                    last, notification.Sequence);
            lastSequence = notification.Sequence;

            var hash = notification.BlockHash.ToString();
            await bus.Publish(
                new BlockAnnounced(hash, notification.Sequence),
                context => context.MessageId = MessageIds.FromKey($"block:{hash}"),
                stoppingToken);
        }
    }
}
