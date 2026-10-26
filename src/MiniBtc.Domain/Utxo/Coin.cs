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
}
