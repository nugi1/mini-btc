using MiniBtc.Application.Abstractions;
using MiniBtc.Application.Unconfirmed;
using MiniBtc.Domain.Blocks;
using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Scripts;
using MiniBtc.Domain.Serialization;
using MiniBtc.Domain.Transactions;
using MiniBtc.Domain.Utxo;

namespace MiniBtc.Application.Explorer;

/// <summary>
/// The explorer's questions, answered from the node's own data: the stored chain (decoded here with
/// the domain's parser, never Core's JSON) and the mempool view. Core is asked only for its height.
/// </summary>
public sealed class ExplorerQueries(
    IExplorerReadModel chain,
    IUtxoStats utxos,
    ICoinLookup coins,
    MempoolState mempool,
    IBitcoinCoreRpc core,
    NetworkParams network)
{
    public async Task<ChainSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var summary = await utxos.GetSummaryAsync(cancellationToken);
        var tip = (await chain.GetLatestBlocksAsync(1, null, cancellationToken)).FirstOrDefault();
        int? coreHeight;
        try
        {
            coreHeight = (await core.GetBlockchainInfoAsync(cancellationToken)).Blocks;
        }
        catch (Exception ex) when (ex is BitcoinCoreRpcException or HttpRequestException)
        {
            coreHeight = null; // the explorer still works on our own data while Core is away
        }

        return new ChainSummary(
            summary.TipHeight,
            summary.TipHash.ToString(),
            tip is null ? DateTimeOffset.UnixEpoch : TimeOf(tip.Header),
            summary.CoinCount,
            summary.TotalAmount,
            mempool.Read(view => view.Count),
            coreHeight,
            network.Name);
    }

    public async Task<IReadOnlyList<BlockRow>> GetLatestBlocksAsync(int limit, int? belowHeight, CancellationToken cancellationToken = default)
    {
        var rows = await chain.GetLatestBlocksAsync(Math.Clamp(limit, 1, 100), belowHeight, cancellationToken);
        return rows.Select(b => new BlockRow(b.Hash.ToString(), b.Height, TimeOf(b.Header), b.TransactionCount, b.Size, b.Fees)).ToList();
    }

    /// <summary>By height (digits) or by hash (64 hex characters).</summary>
    public async Task<BlockView?> GetBlockAsync(string id, CancellationToken cancellationToken = default)
    {
        StoredBlockDetail? stored;
        if (int.TryParse(id, out var height) && height >= 0)
            stored = await chain.GetBlockAsync(null, height, cancellationToken);
        else if (TryParseHash(id, out var hash))
            stored = await chain.GetBlockAsync(hash, null, cancellationToken);
        else
            return null;
        if (stored is null) return null;

        var header = BlockHeader.ReadFrom(new ByteReader(stored.Block.Header));
        var transactions = stored.Transactions.Select(raw => Transaction.Parse(raw)).ToList();
        var rows = transactions.Select((tx, position) =>
        {
            Coin? Spent(int input) => stored.SpentCoins.GetValueOrDefault((position, input));
            var fee = tx.IsCoinbase ? null : Fee(tx, Spent);
            return new TxRow(tx.Txid.ToString(), tx.IsCoinbase, tx.Inputs.Count, tx.Outputs.Count, tx.Outputs.Sum(o => o.Value), fee, Vsize(tx));
        }).ToList();

        var strippedSize = BlockHeader.Size + CompactSize.SizeOf((ulong)transactions.Count)
                           + transactions.Sum(tx => tx.ToBytes(includeWitness: false).Length);
        return new BlockView(
            stored.Block.Hash.ToString(),
            stored.Block.Height,
            stored.TipHeight - stored.Block.Height + 1,
            header.PreviousBlockHash.ToString(),
            stored.NextHash?.ToString(),
            header.MerkleRoot.ToString(),
            DateTimeOffset.FromUnixTimeSeconds(header.Time),
            header.Version.ToString("x8"),
            header.Bits.ToString("x8"),
            header.Nonce,
            stored.Block.Size,
            strippedSize * (ConsensusConstants.WitnessScaleFactor - 1) + stored.Block.Size,
            stored.Block.Fees,
            network.GetBlockSubsidy(stored.Block.Height),
            rows);
    }

    /// <summary>A confirmed transaction from the stored chain, else one in the mempool view, else null.</summary>
    public async Task<TxView?> GetTransactionAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!TryParseHash(id, out var txid)) return null;

        var stored = await chain.GetTransactionAsync(txid, cancellationToken);
        if (stored is not null)
        {
            var tx = Transaction.Parse(stored.Raw);
            return Describe(
                tx,
                status: "confirmed",
                stored.BlockHash.ToString(),
                stored.BlockHeight,
                stored.TipHeight - stored.BlockHeight + 1,
                input => tx.IsCoinbase ? null : stored.SpentCoins[input],
                output => tx.Outputs[output].IsUnspendable ? null : !stored.UnspentOutputs.Contains(output));
        }

        var pending = mempool.Read(view => view.Get(txid));
        if (pending is null) return null;
        var spent = await ResolveInputsAsync(pending, cancellationToken);
        return Describe(pending, "mempool", null, null, null, input => spent[input], _ => null);
    }

    public async Task<IReadOnlyList<MempoolRow>> GetMempoolAsync(CancellationToken cancellationToken = default)
    {
        var pending = mempool.Read(view => view.Transactions.ToList());
        var rows = new List<MempoolRow>(pending.Count);
        foreach (var tx in pending)
        {
            var spent = await ResolveInputsAsync(tx, cancellationToken);
            rows.Add(new MempoolRow(tx.Txid.ToString(), Vsize(tx), tx.Inputs.Count, tx.Outputs.Count,
                tx.Outputs.Sum(o => o.Value), Fee(tx, input => spent[input])));
        }
        return rows.OrderByDescending(r => r.Fee is { } fee ? (double)fee / r.Vsize : 0).ToList();
    }

    /// <summary>The coins an unconfirmed transaction spends: confirmed ones from the UTXO set, the rest from mempool parents.</summary>
    private async Task<Coin?[]> ResolveInputsAsync(Transaction tx, CancellationToken cancellationToken)
    {
        var snapshot = await coins.GetCoinsAsync(tx.Inputs.Select(i => i.PrevOut).ToList(), cancellationToken);
        return mempool.Read(view => tx.Inputs.Select(input =>
        {
            if (snapshot.Coins.TryGetValue(input.PrevOut, out var coin)) return coin;
            var parent = view.Get(input.PrevOut.Txid);
            return parent is not null && input.PrevOut.Index < parent.Outputs.Count
                ? new Coin(parent.Outputs[(int)input.PrevOut.Index], Domain.Unconfirmed.Mempool.MempoolHeight, IsCoinbase: false)
                : null;
        }).ToArray());
    }

    private TxView Describe(
        Transaction tx,
        string status,
        string? blockHash,
        int? blockHeight,
        int? confirmations,
        Func<int, Coin?> spentCoin,
        Func<int, bool?> outputSpent)
    {
        var inputs = tx.Inputs.Select((input, index) =>
        {
            var coin = tx.IsCoinbase ? null : spentCoin(index);
            var script = input.ScriptSig.Span;
            ScriptParser.TryParsePushes(script, out var pushes);
            return new TxInputView(
                index,
                tx.IsCoinbase,
                tx.IsCoinbase ? null : input.PrevOut.Txid.ToString(),
                tx.IsCoinbase ? null : input.PrevOut.Index,
                coin?.Value,
                coin is null ? null : OutputScripts.ToAddress(coin.Output.ScriptPubKey.Span, network),
                coin is null ? null : OutputScripts.CoreName(OutputScripts.Classify(coin.Output.ScriptPubKey.Span)),
                Convert.ToHexStringLower(script),
                pushes.Select(push => Convert.ToHexStringLower(push)).ToList(),
                input.Witness.Items.Select(item => Convert.ToHexStringLower(item.Span)).ToList(),
                input.Sequence);
        }).ToList();

        var outputs = tx.Outputs.Select((output, index) => new TxOutputView(
            index,
            output.Value,
            Convert.ToHexStringLower(output.ScriptPubKey.Span),
            OutputScripts.CoreName(OutputScripts.Classify(output.ScriptPubKey.Span)),
            OutputScripts.ToAddress(output.ScriptPubKey.Span, network),
            outputSpent(index))).ToList();

        var totalIn = inputs.Sum(i => i.Value ?? 0);
        var totalOut = outputs.Sum(o => o.Value);
        var size = tx.ToBytes().Length;
        return new TxView(
            tx.Txid.ToString(),
            tx.Wtxid.ToString(),
            status,
            blockHash,
            blockHeight,
            confirmations,
            tx.Version,
            tx.LockTime,
            size,
            Vsize(tx),
            Weight(tx),
            tx.IsCoinbase,
            totalIn,
            totalOut,
            tx.IsCoinbase ? null : Fee(tx, spentCoin),
            inputs,
            outputs,
            Convert.ToHexStringLower(tx.ToBytes()));
    }

    private static long? Fee(Transaction tx, Func<int, Coin?> spentCoin)
    {
        long valueIn = 0;
        for (var i = 0; i < tx.Inputs.Count; i++)
        {
            if (spentCoin(i) is not { } coin) return null;
            valueIn += coin.Value;
        }
        return valueIn - tx.Outputs.Sum(o => o.Value);
    }

    /// <summary>BIP141 weight: the bytes without witnesses count four times, witness bytes once.</summary>
    private static int Weight(Transaction tx)
    {
        var stripped = tx.ToBytes(includeWitness: false).Length;
        return stripped * (ConsensusConstants.WitnessScaleFactor - 1) + tx.ToBytes().Length;
    }

    /// <summary>Virtual size: weight / 4, rounded up. Equal to the size for a transaction without witnesses.</summary>
    private static int Vsize(Transaction tx) => (Weight(tx) + ConsensusConstants.WitnessScaleFactor - 1) / ConsensusConstants.WitnessScaleFactor;

    private static DateTimeOffset TimeOf(byte[] header) =>
        DateTimeOffset.FromUnixTimeSeconds(BlockHeader.ReadFrom(new ByteReader(header)).Time);

    private static bool TryParseHash(string text, out UInt256 hash)
    {
        hash = default;
        if (text.Length != 64 || !text.All(Uri.IsHexDigit)) return false;
        hash = UInt256.Parse(text.ToLowerInvariant());
        return true;
    }
}
