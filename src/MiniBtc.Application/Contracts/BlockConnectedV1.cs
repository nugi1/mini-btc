using Patterns.DDD.Application;

namespace MiniBtc.Application.Contracts;

/// <summary>
/// Published once the node has validated a block with its own rules and committed it. This is a
/// contract other services rely on (the explorer, the future payments service), so it never changes
/// in place: a new shape becomes BlockConnectedV2. Only primitives, so consumers need no Bitcoin types.
/// </summary>
/// <param name="BlockHash">Display-order hex, as getblockhash and explorers show it.</param>
/// <param name="Height">Its height in the active chain.</param>
/// <param name="TransactionCount">Transactions in the block, coinbase included.</param>
public sealed record BlockConnectedV1(string BlockHash, int Height, int TransactionCount) : IIntegrationEvent;
