namespace MiniBtc.Application.Contracts;

/// <summary>
/// Core added a transaction to its mempool (ZMQ sequence "A"). Like BlockAnnounced, a hint: only the
/// txid travels, and the node fetches the transaction itself. Its message id comes from the txid and
/// the mempool sequence, so a redelivery is dropped by the inbox while a later re-add of the same
/// transaction (after a reorg, say) is a new message.
/// </summary>
/// <param name="Txid">Display-order hex.</param>
/// <param name="MempoolSequence">Core's mempool sequence number of this event.</param>
public sealed record MempoolTxAdded(string Txid, ulong MempoolSequence);

/// <summary>
/// Core removed a transaction from its mempool for a reason other than a block confirming it (ZMQ
/// sequence "R"): replaced, expired, evicted, or conflicted by a block.
/// </summary>
public sealed record MempoolTxRemoved(string Txid, ulong MempoolSequence);
