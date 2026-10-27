using MiniBtc.Domain.Hashing;

namespace MiniBtc.Application.Abstractions;

/// <summary>
/// The port to Bitcoin Core's JSON-RPC. Only what the node needs: to follow Core's chain (where the
/// tip is, which block sits at a height, that block's raw bytes), to follow its mempool, and to hand
/// it transactions. Blocks and transactions come back as bytes on purpose: the node decodes and
/// validates them with its own code, and never trusts Core's decoded JSON.
/// </summary>
public interface IBitcoinCoreRpc
{
    /// <summary>getblockchaininfo</summary>
    Task<BlockchainInfo> GetBlockchainInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>getblockhash &lt;height&gt; — the hash of the block at that height on Core's active chain.</summary>
    Task<UInt256> GetBlockHashAsync(int height, CancellationToken cancellationToken = default);

    /// <summary>getblock &lt;hash&gt; 0 — the block exactly as serialized on the wire, witnesses included.</summary>
    Task<byte[]> GetRawBlockAsync(UInt256 blockHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// sendrawtransaction &lt;hex&gt; — Core validates the transaction, adds it to its mempool and relays
    /// it. Returns the txid. A rejection is a <see cref="BitcoinCoreRpcException"/>: -26 with Core's
    /// reject reason, -25 for missing inputs, -27 if it is already confirmed, -22 if it won't decode.
    /// </summary>
    Task<UInt256> SendRawTransactionAsync(string hex, CancellationToken cancellationToken = default);

    /// <summary>getrawtransaction &lt;txid&gt; — the serialized transaction (mempool, or any block with -txindex).</summary>
    Task<byte[]> GetRawTransactionAsync(UInt256 txid, CancellationToken cancellationToken = default);

    /// <summary>
    /// getrawmempool false true — the txids in Core's mempool and the mempool sequence number they
    /// reflect. ZMQ sequence events at or below that number are already included.
    /// </summary>
    Task<RawMempool> GetRawMempoolAsync(CancellationToken cancellationToken = default);
}

/// <param name="Txids">Every transaction in Core's mempool.</param>
/// <param name="MempoolSequence">Core's mempool sequence number at that moment.</param>
public sealed record RawMempool(IReadOnlyList<UInt256> Txids, ulong MempoolSequence);

/// <summary>The parts of getblockchaininfo the node uses.</summary>
/// <param name="Chain">"main", "test", "testnet4", "signet" or "regtest".</param>
/// <param name="Blocks">Height of the most-work fully validated block: Core's tip.</param>
/// <param name="Headers">Height of the most-work header Core knows; above Blocks while Core syncs.</param>
/// <param name="BestBlockHash">Hash of the tip.</param>
public sealed record BlockchainInfo(string Chain, int Blocks, int Headers, UInt256 BestBlockHash);
