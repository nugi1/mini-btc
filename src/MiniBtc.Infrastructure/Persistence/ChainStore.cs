using Microsoft.EntityFrameworkCore;
using MiniBtc.Application.Abstractions;
using MiniBtc.Domain.Blocks;
using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Hashing;
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
