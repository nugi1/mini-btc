using MiniBtc.Domain.Blocks;
using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Transactions;
using MiniBtc.Domain.Utxo;

namespace MiniBtc.Domain.Tests.TestData;

/// <summary>
/// Builds small regtest chains for tests. Every block is really mined: on regtest about every
/// second nonce meets the target, so finding one takes a couple of tries.
/// </summary>
internal static class TestChain
{
    public const uint RegtestBits = 0x207fffff;

    /// <summary>OP_TRUE: a script anyone can spend. Script checks come later; here only coins matter.</summary>
    public static readonly byte[] AnyoneCanSpend = [0x51];

    /// <summary>OP_RETURN with a payload: provably unspendable, never stored.</summary>
    public static readonly byte[] OpReturn = [0x6a, 0x04, 0xde, 0xad, 0xbe, 0xef];

    public static UInt256 Genesis => NetworkParams.Regtest.GenesisHash;

    /// <summary>
    /// A coinbase whose scriptSig pushes the height (as BIP34 requires), so coinbases at different
    /// heights have different txids.
    /// </summary>
    public static Transaction Coinbase(int height, long value = 50 * ConsensusConstants.Coin, params TxOut[] extraOutputs) =>
        new(2,
            [new TxIn(OutPoint.Null, [0x03, (byte)height, (byte)(height >> 8), (byte)(height >> 16)])],
            [new TxOut(value, AnyoneCanSpend), .. extraOutputs],
            0);

    /// <summary>Spends the given outpoints into outputs of the given values.</summary>
    public static Transaction Spend(OutPoint[] inputs, params long[] outputValues) =>
        new(2,
            inputs.Select(prevOut => new TxIn(prevOut, ReadOnlySpan<byte>.Empty)),
            outputValues.Select(value => new TxOut(value, AnyoneCanSpend)),
            0);

    /// <summary>A mined block with the correct merkle root.</summary>
    public static Block MinedBlock(UInt256 previous, params Transaction[] transactions) =>
        MinedBlockWithMerkleRoot(previous, MerkleTree.ComputeRoot([.. transactions.Select(tx => tx.Txid)], out _), transactions);

    /// <summary>A mined block whose header commits to <paramref name="merkleRoot"/>, right or wrong.</summary>
    public static Block MinedBlockWithMerkleRoot(UInt256 previous, UInt256 merkleRoot, params Transaction[] transactions) =>
        new(Mine(previous, merkleRoot, RegtestBits), transactions);

    /// <summary>
    /// A UTXO set at height <paramref name="tipHeight"/> whose block 1 paid <paramref name="Coinbase"/>.
    /// Blocks 2…tip are coinbase-only. With tipHeight 100 the next block (101) may spend block 1's
    /// coinbase: 101 − 1 = 100 = COINBASE_MATURITY.
    /// </summary>
    public static (UtxoSet Utxos, Transaction Coinbase) ChainWithCoinbaseAtHeight1(int tipHeight = 100)
    {
        var utxos = UtxoSet.AtGenesis(NetworkParams.Regtest);
        var coinbase = Coinbase(1);
        Connect(utxos, MinedBlock(Genesis, coinbase));
        for (var height = 2; height <= tipHeight; height++)
            Connect(utxos, MinedBlock(utxos.Tip.Hash, Coinbase(height)));
        return (utxos, coinbase);
    }

    /// <summary>Connects a block the test expects to be valid, and fails loudly if it isn't.</summary>
    public static BlockUndoAndHash Connect(UtxoSet utxos, Block block)
    {
        var result = utxos.Connect(block);
        if (result.IsFailure) throw new InvalidOperationException($"test chain block rejected: {result.Error}");
        return new BlockUndoAndHash(result.Value, block.Hash);
    }

    public sealed record BlockUndoAndHash(BlockUndo Undo, UInt256 Hash);

    /// <summary>Grinds the nonce until the header hash meets the target its bits encode.</summary>
    public static BlockHeader Mine(UInt256 previous, UInt256 merkleRoot, uint bits)
    {
        var target = CompactTarget.Decode(bits, out _, out _);
        for (uint nonce = 0; ; nonce++)
        {
            var header = new BlockHeader(0x20000000, previous, merkleRoot, time: 1_700_000_000, bits, nonce);
            if (header.Hash <= target) return header;
        }
    }

    /// <summary>A header that fails its own target: the nonce is ground until the hash is too high.</summary>
    public static BlockHeader MineBadly(UInt256 previous, UInt256 merkleRoot, uint bits)
    {
        var target = CompactTarget.Decode(bits, out _, out _);
        for (uint nonce = 0; ; nonce++)
        {
            var header = new BlockHeader(0x20000000, previous, merkleRoot, time: 1_700_000_000, bits, nonce);
            if (header.Hash > target) return header;
        }
    }
}
