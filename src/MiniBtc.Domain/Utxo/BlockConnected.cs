using MiniBtc.Domain.Hashing;
using Patterns.DDD;

namespace MiniBtc.Domain.Utxo;

/// <summary>A block was applied to the UTXO set and is now the tip.</summary>
public sealed record BlockConnected(UInt256 BlockHash, int Height, int TransactionCount) : IDomainEvent;

/// <summary>The tip block was undone; its parent is the tip again. Its transactions are unconfirmed.</summary>
public sealed record BlockDisconnected(UInt256 BlockHash, int Height, int TransactionCount) : IDomainEvent;
