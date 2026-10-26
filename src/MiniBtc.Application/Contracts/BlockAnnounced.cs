namespace MiniBtc.Application.Contracts;

/// <summary>
/// Bitcoin Core announced a new block over ZMQ. A hint, not data: it only says "look again", and the
/// node then syncs to Core's tip over RPC. Its message id comes from the block hash, so the inbox
/// drops redeliveries of the same announcement.
/// </summary>
/// <param name="BlockHash">Display-order hex of the announced block.</param>
/// <param name="ZmqSequence">Core's per-topic counter; a gap means notifications were lost.</param>
public sealed record BlockAnnounced(string BlockHash, uint ZmqSequence);
