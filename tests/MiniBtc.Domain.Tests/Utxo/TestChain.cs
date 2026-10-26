using MiniBtc.Domain.Blocks;
using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Transactions;

namespace MiniBtc.Domain.Tests.Utxo;

/// <summary>
/// Builds small regtest chains for UTXO tests. Blocks carry a correct merkle root but no real
/// proof of work: the UTXO set doesn't check PoW (the header rules do, in a later commit).
/// </summary>
internal static class TestChain
{
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

    public static Block Block(UInt256 previous, params Transaction[] transactions)
    {
        var merkleRoot = MerkleTree.ComputeRoot([.. transactions.Select(tx => tx.Txid)], out _);
        var header = new BlockHeader(0x20000000, previous, merkleRoot, time: 1_700_000_000, bits: 0x207fffff, nonce: 0);
        return new Block(header, transactions);
    }
}
