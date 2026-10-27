namespace MiniBtc.Infrastructure.Messaging;

/// <summary>
/// How often MassTransit's delivery service looks for outbox rows to publish. Our rows are written by
/// the Patterns.DDD save interceptor, not by MassTransit's scoped publish endpoint, so nothing wakes the
/// delivery service early: an event waits up to one QueryDelay. MassTransit's default is meant for busy
/// production services; a block-by-block demo wants events within a fraction of a second.
/// </summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public TimeSpan QueryDelay { get; set; } = TimeSpan.FromMilliseconds(250);
}
