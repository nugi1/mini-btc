using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Transactions;

namespace MiniBtc.Domain.Utxo;

/// <summary>
/// An unspent transaction output as the UTXO set keeps it (Core: Coin in coins.h): the output
/// itself plus the two facts later rules need about where it came from. Height drives coinbase
/// maturity; the coinbase flag says whether maturity applies at all.
/// </summary>
/// <param name="Output">The amount and locking script.</param>
/// <param name="Height">Height of the block that created it.</param>
/// <param name="IsCoinbase">Created by a coinbase: spendable only 100 blocks later.</param>
public sealed record Coin(TxOut Output, int Height, bool IsCoinbase)
{
    public long Value => Output.Value;

    /// <summary>
    /// The first height of a block that may spend this coinbase output: 100 blocks above its own.
    /// Null for other coins, which are spendable at once.
    /// </summary>
    public int? MaturityHeight => IsCoinbase ? Height + ConsensusConstants.CoinbaseMaturity : null;

    /// <summary>
    /// Whether a transaction in a block at <paramref name="spendHeight"/> may spend this coin
    /// (Core: CheckTxInputs). The mempool asks the same with tip height + 1, the block a new
    /// transaction can first get into.
    /// </summary>
    public bool IsSpendableAt(int spendHeight) =>
        !IsCoinbase || spendHeight - Height >= ConsensusConstants.CoinbaseMaturity;
}
