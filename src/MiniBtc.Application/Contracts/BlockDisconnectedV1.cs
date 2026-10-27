using Patterns.DDD.Application;

namespace MiniBtc.Application.Contracts;

/// <summary>
/// Published once the node has undone a block because Core's chain no longer contains it (a reorg),
/// in the same database transaction as the undo. Its transactions are unconfirmed again.
/// </summary>
/// <param name="BlockHash">Display-order hex of the block that left the chain.</param>
/// <param name="Height">The height it had; its parent is now the tip.</param>
/// <param name="TransactionCount">Transactions in the block, coinbase included.</param>
public sealed record BlockDisconnectedV1(string BlockHash, int Height, int TransactionCount) : IIntegrationEvent;
