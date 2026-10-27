using MiniBtc.Application.Abstractions;
using MiniBtc.Application.Unconfirmed;
using MiniBtc.Domain.Blocks;
using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Serialization;
using MiniBtc.Domain.Utxo;
using MiniBtc.Domain.Validation;
using Patterns.DDD;
using Patterns.DDD.Application;

namespace MiniBtc.Application.ChainSync;

/// <summary>
/// Brings our chain level with Core's: every block above our tip is fetched as raw bytes, decoded and
/// validated by our own code, applied to the UTXO set and committed, one database transaction per block.
/// If Core has left our branch (a reorg, or invalidateblock), our blocks that Core no longer has are
/// disconnected first, newest first, each in its own transaction, until our tip is on Core's chain.
/// It does not care what triggered it (startup, a ZMQ notification, a timer): it always walks from our
/// tip to Core's tip, so it is safe to run any number of times. The mempool view follows each block.
/// </summary>
public sealed class ChainSynchronizer(
    IBitcoinCoreRpc core,
    IChainStore store,
    IUnitOfWork unitOfWork,
    NetworkParams network,
    ChainSyncOptions options,
    MempoolState mempool)
{
    /// <summary>
    /// Fails with SyncErrors when Core is not the chain we validate, or with the block's reject code
    /// when our rules reject a block Core accepted. That is a disagreement between two implementations,
    /// so the run stops there and never skips the block.
    /// </summary>
    public async Task<Result<SyncReport>> SyncToTipAsync(CancellationToken cancellationToken = default)
    {
        var info = await GetBlockchainInfoAfterWarmupAsync(cancellationToken);
        if (info.Chain != options.ExpectedChain)
            return new Error(SyncErrors.ChainMismatch, $"Core follows '{info.Chain}', not '{options.ExpectedChain}'");

        var coreGenesis = await core.GetBlockHashAsync(0, cancellationToken);
        if (coreGenesis != network.GenesisHash)
            return new Error(SyncErrors.GenesisMismatch, $"Core's genesis is {coreGenesis}, ours is {network.GenesisHash}");

        var utxos = await store.LoadAsync(network, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken); // records genesis on a fresh database; otherwise a no-op

        var (connected, disconnected) = (0, 0);
        while (true)
        {
            // Back off our branch until our tip is a block Core's active chain contains.
            while (utxos.Tip.Height > 0 && !await IsOnCoresChainAsync(utxos.Tip, info, cancellationToken))
            {
                var undone = await DisconnectTipAsync(utxos, cancellationToken);
                if (undone.IsFailure)
                    return undone.Error;
                disconnected++;
            }

            var switchedBranch = false;
            while (utxos.Tip.Height < info.Blocks)
            {
                var step = await ConnectNextAsync(utxos, cancellationToken);
                if (step.IsFailure && step.Error.Code == HeaderErrors.PrevBlockNotFound)
                {
                    switchedBranch = true; // Core reorganized while we were connecting: look again
                    break;
                }
                if (step.IsFailure)
                    return step.Error;
                connected++;
            }

            // Core may have mined, or reorganized, while we were syncing: look again before declaring ourselves level.
            info = await core.GetBlockchainInfoAsync(cancellationToken);
            if (!switchedBranch && utxos.Tip.Height == info.Blocks && utxos.Tip.Hash == info.BestBlockHash)
                return new SyncReport(connected, utxos.Tip, info.Blocks, disconnected);
        }
    }

    /// <summary>Is our tip in Core's active chain? At Core's height compare with its tip; below it, ask.</summary>
    private async Task<bool> IsOnCoresChainAsync(ChainTip tip, BlockchainInfo info, CancellationToken cancellationToken)
    {
        if (tip.Height > info.Blocks) return false;
        if (tip.Height == info.Blocks) return tip.Hash == info.BestBlockHash;
        return await core.GetBlockHashAsync(tip.Height, cancellationToken) == tip.Hash;
    }

    private async Task<Result> ConnectNextAsync(UtxoSet utxos, CancellationToken cancellationToken)
    {
        var height = utxos.Tip.Height + 1;
        var hash = await core.GetBlockHashAsync(height, cancellationToken);
        var raw = await core.GetRawBlockAsync(hash, cancellationToken);

        Block block;
        try
        {
            block = Block.Parse(raw);
        }
        catch (DecodeException ex)
        {
            return new Error(SyncErrors.UndecodableBlock, $"block {hash} at height {height}: {ex.Reason}");
        }

        if (block.Hash != hash)
            return new Error(SyncErrors.BlockHashMismatch, $"asked for {hash} at height {height}, got {block.Hash}");

        var result = utxos.Connect(block);
        if (result.IsFailure)
            return new Error(result.Error.Code, $"block {hash} at height {height}: {result.Error.Message}");

        store.AddConnectedBlock(block, result.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        mempool.Update(view => view.RemoveForBlock(block));
        return Result.Success();
    }

    private async Task<Result> DisconnectTipAsync(UtxoSet utxos, CancellationToken cancellationToken)
    {
        var height = utxos.Tip.Height;
        var stored = await store.LoadTipBlockAsync(cancellationToken);
        var result = utxos.Disconnect(stored.Block, stored.Undo);
        if (result.IsFailure)
            return new Error(result.Error.Code, $"block {stored.Block.Hash} at height {height}: {result.Error.Message}");

        store.AddDisconnectedBlock(stored.Block, result.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        mempool.Update(view => view.ReturnFromDisconnectedBlock(stored.Block));
        return Result.Success();
    }

    private async Task<BlockchainInfo> GetBlockchainInfoAfterWarmupAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await core.GetBlockchainInfoAsync(cancellationToken);
            }
            catch (BitcoinCoreRpcException ex) when (ex.Code == RpcErrorCodes.InWarmup && attempt < options.WarmupMaxAttempts)
            {
                await Task.Delay(options.WarmupRetryDelay, cancellationToken);
            }
        }
    }
}
