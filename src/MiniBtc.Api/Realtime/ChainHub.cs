using Microsoft.AspNetCore.SignalR;

namespace MiniBtc.Api.Realtime;

/// <summary>
/// Live chain events for the explorer, pushed by the server; clients only listen. Block events
/// come from the outbox through RabbitMQ (so they are sent only once the block is committed),
/// transaction events from the broadcast gateway and from Core's mempool notifications.
/// </summary>
public sealed class ChainHub : Hub<IChainClient>
{
    public const string Path = "/hubs/chain";
}

/// <summary>The messages clients receive; each method name is the event name on the wire.</summary>
public interface IChainClient
{
    Task BlockConnected(BlockEvent block);
    Task BlockDisconnected(BlockEvent block);
    Task TxAccepted(TxAcceptedEvent tx);
    Task TxRejected(TxRejectedEvent rejection);
    Task TxRemoved(TxRemovedEvent tx);
}

public sealed record BlockEvent(string Hash, int Height, int TransactionCount);

/// <param name="Source">"gateway" when it came through POST /tx, "core" when Core announced it.</param>
public sealed record TxAcceptedEvent(string Txid, string Source);

/// <param name="Txid">Null when the bytes didn't even decode.</param>
/// <param name="RejectedBy">"node" or "core".</param>
/// <param name="Code">Core's reject reason.</param>
public sealed record TxRejectedEvent(string? Txid, string RejectedBy, string Code, string Message);

public sealed record TxRemovedEvent(string Txid);
