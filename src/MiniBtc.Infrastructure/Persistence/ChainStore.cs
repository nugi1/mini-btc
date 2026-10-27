using Microsoft.EntityFrameworkCore;
using MiniBtc.Application.Abstractions;
using MiniBtc.Domain.Blocks;
using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Serialization;
using MiniBtc.Domain.Transactions;
using MiniBtc.Domain.Utxo;

namespace MiniBtc.Infrastructure.Persistence;

/// <summary>
/// Maps the UTXO set and connected blocks to rows. It only stages changes in the DbContext; the
/// caller commits them through IUnitOfWork, so a block is stored completely or not at all.
/// </summary>
public sealed class ChainStore(MiniBtcDbContext db) : IChainStore
{
    private ChainStateRecord? _state;
    private UtxoSet? _utxos;
    private LoadedTipRows? _loadedTip;

    public async Task<UtxoSet> LoadAsync(NetworkParams network, CancellationToken cancellationToken = default)
    {
        _state = await db.ChainStates.SingleOrDefaultAsync(s => s.Network == network.Name, cancellationToken);
        if (_state is null)
        {
            _utxos = UtxoSet.AtGenesis(network);
            _state = new ChainStateRecord { Network = network.Name };
            db.ChainStates.Add(_state);
            AddBlockRows(network.GenesisBlock, height: 0, fees: 0);
        }
        else
        {
            var coins = await db.Coins.AsNoTracking().ToListAsync(cancellationToken);
            var tip = new ChainTip(new UInt256(_state.TipHash), _state.TipHeight);
            _utxos = UtxoSet.Restore(network, tip, coins.Select(ToCoinEntry));
        }

        _state.Track(_utxos);
        return _utxos;
    }

    public void AddConnectedBlock(Block block, ConnectedBlock connected)
    {
        if (_state is null || _utxos is null)
            throw new InvalidOperationException("Load the UTXO set before adding blocks.");
        if (_utxos.Tip.Hash != block.Hash)
            throw new InvalidOperationException($"Block {block.Hash} is not the loaded set's tip {_utxos.Tip.Hash}.");

        var height = _utxos.Tip.Height;
        var blockHash = block.Hash.ToWireBytes();
        AddBlockRows(block, height, connected.Fees);

        foreach (var spent in connected.Changes.Spent) RemoveCoin(spent);
        foreach (var (outPoint, coin) in connected.Changes.Created) db.Coins.Add(ToRecord(outPoint, coin));

        // TxUndo entries follow the block's non-coinbase transactions in order.
        var undo = connected.Undo.Transactions;
        for (int position = 1, undoIndex = 0; position < block.Transactions.Count; position++, undoIndex++)
        {
            var inputs = block.Transactions[position].Inputs;
            var spentCoins = undo[undoIndex].SpentCoins;
            for (var input = 0; input < inputs.Count; input++)
            {
                var (prevOut, coin) = (inputs[input].PrevOut, spentCoins[input]);
                db.UndoCoins.Add(new UndoCoinRecord
                {
                    BlockHash = blockHash, TxPosition = position, InputIndex = input,
                    Txid = prevOut.Txid.ToWireBytes(), Vout = (int)prevOut.Index,
                    Value = coin.Value, ScriptPubKey = coin.Output.ScriptPubKey.ToArray(),
                    Height = coin.Height, IsCoinbase = coin.IsCoinbase,
                });
            }
        }

        foreach (var script in connected.UnverifiedScripts)
        {
            db.UnverifiedScripts.Add(new UnverifiedScriptRecord
            {
                BlockHash = blockHash, Txid = script.Txid.ToWireBytes(), InputIndex = script.InputIndex, Reason = script.Reason,
            });
        }

        _state.SyncTip();
    }

    public async Task<StoredBlock> LoadTipBlockAsync(CancellationToken cancellationToken = default)
    {
        if (_utxos is null)
            throw new InvalidOperationException("Load the UTXO set before reading its tip block.");

        // Tracked, not AsNoTracking: if the block is disconnected these rows are deleted in the same save.
        var hash = _utxos.Tip.Hash.ToWireBytes();
        var block = await db.Blocks.SingleAsync(b => b.Hash == hash, cancellationToken);
        var transactions = await db.Transactions.Where(t => t.BlockHash == hash).OrderBy(t => t.Position).ToListAsync(cancellationToken);
        var undoCoins = await db.UndoCoins.Where(u => u.BlockHash == hash)
            .OrderBy(u => u.TxPosition).ThenBy(u => u.InputIndex).ToListAsync(cancellationToken);
        var unverified = await db.UnverifiedScripts.Where(u => u.BlockHash == hash).ToListAsync(cancellationToken);
        _loadedTip = new LoadedTipRows(block, transactions, undoCoins, unverified);

        var header = BlockHeader.ReadFrom(new ByteReader(block.Header));
        var stored = new Block(header, transactions.Select(t => Transaction.Parse(t.Raw)));
        var undo = new BlockUndo(undoCoins
            .GroupBy(u => u.TxPosition)
            .OrderBy(group => group.Key)
            .Select(group => new TxUndo(group.Select(u => new Coin(new TxOut(u.Value, u.ScriptPubKey), u.Height, u.IsCoinbase)))));
        return new StoredBlock(stored, undo);
    }

    public void AddDisconnectedBlock(Block block, DisconnectedBlock disconnected)
    {
        if (_state is null || _utxos is null)
            throw new InvalidOperationException("Load the UTXO set before disconnecting blocks.");
        if (_loadedTip is null || !_loadedTip.Block.Hash.AsSpan().SequenceEqual(block.Hash.ToWireBytes()))
            throw new InvalidOperationException($"Block {block.Hash} was not read with LoadTipBlockAsync.");

        db.Blocks.Remove(_loadedTip.Block);
        db.Transactions.RemoveRange(_loadedTip.Transactions);
        db.UndoCoins.RemoveRange(_loadedTip.UndoCoins);
        db.UnverifiedScripts.RemoveRange(_loadedTip.UnverifiedScripts);
        _loadedTip = null;

        foreach (var removed in disconnected.Changes.Spent) RemoveCoin(removed);
        foreach (var (outPoint, coin) in disconnected.Changes.Created) db.Coins.Add(ToRecord(outPoint, coin));

        _state.SyncTip();
    }

    private sealed record LoadedTipRows(
        BlockRecord Block,
        List<TransactionRecord> Transactions,
        List<UndoCoinRecord> UndoCoins,
        List<UnverifiedScriptRecord> UnverifiedScripts);

    private void AddBlockRows(Block block, int height, long fees)
    {
        var blockHash = block.Hash.ToWireBytes();
        db.Blocks.Add(new BlockRecord
        {
            Hash = blockHash,
            Height = height,
            PreviousHash = block.Header.PreviousBlockHash.ToWireBytes(),
            Header = block.Header.ToBytes(),
            TransactionCount = block.Transactions.Count,
            Size = block.ToBytes().Length,
            Fees = fees,
        });

        for (var position = 0; position < block.Transactions.Count; position++)
        {
            var tx = block.Transactions[position];
            db.Transactions.Add(new TransactionRecord
            {
                BlockHash = blockHash, Position = position,
                Txid = tx.Txid.ToWireBytes(), Wtxid = tx.Wtxid.ToWireBytes(),
                IsCoinbase = tx.IsCoinbase, Raw = tx.ToBytes(),
            });
        }
    }

    private void RemoveCoin(OutPoint outPoint)
    {
        var txid = outPoint.Txid.ToWireBytes();
        var vout = (int)outPoint.Index;
        // The context may already track this row (inserted by an earlier block in the same context);
        // removing a second instance with the same key would throw, so remove the tracked one.
        var tracked = db.Coins.Local.FindEntryUntyped(new object[] { txid, vout });
        db.Coins.Remove(tracked?.Entity ?? new CoinRecord { Txid = txid, Vout = vout });
    }

    private static CoinRecord ToRecord(OutPoint outPoint, Coin coin) => new()
    {
        Txid = outPoint.Txid.ToWireBytes(), Vout = (int)outPoint.Index,
        Value = coin.Value, ScriptPubKey = coin.Output.ScriptPubKey.ToArray(),
        Height = coin.Height, IsCoinbase = coin.IsCoinbase,
    };

    private static KeyValuePair<OutPoint, Coin> ToCoinEntry(CoinRecord record) => new(
        new OutPoint(new UInt256(record.Txid), (uint)record.Vout),
        new Coin(new TxOut(record.Value, record.ScriptPubKey), record.Height, record.IsCoinbase));
}
