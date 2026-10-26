using MiniBtc.Domain.Hashing;

namespace MiniBtc.Application.Abstractions;

/// <summary>
/// The port to Bitcoin Core's JSON-RPC. Only what the node needs to follow Core's chain:
/// where the tip is, which block sits at a height, and that block's raw bytes. Blocks come back
/// as bytes on purpose: the node decodes and validates them with its own code, and never
/// trusts Core's decoded JSON.
/// </summary>
public interface IBitcoinCoreRpc
{
    /// <summary>getblockchaininfo</summary>
    Task<BlockchainInfo> GetBlockchainInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>getblockhash &lt;height&gt; — the hash of the block at that height on Core's active chain.</summary>
    Task<UInt256> GetBlockHashAsync(int height, CancellationToken cancellationToken = default);

    /// <summary>getblock &lt;hash&gt; 0 — the block exactly as serialized on the wire, witnesses included.</summary>
    Task<byte[]> GetRawBlockAsync(UInt256 blockHash, CancellationToken cancellationToken = default);
}

/// <summary>The parts of getblockchaininfo the node uses.</summary>
/// <param name="Chain">"main", "test", "testnet4", "signet" or "regtest".</param>
/// <param name="Blocks">Height of the most-work fully validated block: Core's tip.</param>
/// <param name="Headers">Height of the most-work header Core knows; above Blocks while Core syncs.</param>
/// <param name="BestBlockHash">Hash of the tip.</param>
public sealed record BlockchainInfo(string Chain, int Blocks, int Headers, UInt256 BestBlockHash);
