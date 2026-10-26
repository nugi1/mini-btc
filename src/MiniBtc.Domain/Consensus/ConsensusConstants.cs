namespace MiniBtc.Domain.Consensus;

/// <summary>Network-independent consensus constants, as Bitcoin Core v30.0 defines them.</summary>
public static class ConsensusConstants
{
    /// <summary>Satoshis per bitcoin (consensus/amount.h COIN).</summary>
    public const long Coin = 100_000_000;

    /// <summary>
    /// No single output, and no transaction's total output, may exceed this (consensus/amount.h
    /// MAX_MONEY). It is a sanity bound, not the actual supply.
    /// </summary>
    public const long MaxMoney = 21_000_000 * Coin;

    /// <summary>
    /// A coinbase output created at height h can be spent only in a block at height h + 100 or later
    /// (consensus/consensus.h COINBASE_MATURITY).
    /// </summary>
    public const int CoinbaseMaturity = 100;

    /// <summary>consensus/consensus.h MAX_BLOCK_WEIGHT (BIP141): 4 weight units per non-witness byte, 1 per witness byte.</summary>
    public const int MaxBlockWeight = 4_000_000;

    /// <summary>consensus/consensus.h WITNESS_SCALE_FACTOR: how much more a non-witness byte weighs.</summary>
    public const int WitnessScaleFactor = 4;

    /// <summary>Core: MoneyRange — 0 ≤ value ≤ MAX_MONEY.</summary>
    public static bool IsMoneyRange(long value) => value is >= 0 and <= MaxMoney;
}
