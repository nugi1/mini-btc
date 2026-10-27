using MiniBtc.Domain.Transactions;
using MiniBtc.Domain.Utxo;

namespace MiniBtc.Domain.Tests.Utxo;

public class CoinTests
{
    private static readonly TxOut Output = new(50_0000_0000, [0x51]);

    [Fact]
    public void A_coinbase_from_height_1_is_spendable_from_block_101_on()
    {
        var coin = new Coin(Output, Height: 1, IsCoinbase: true);

        Assert.Equal(101, coin.MaturityHeight);
        Assert.False(coin.IsSpendableAt(100)); // depth 99
        Assert.True(coin.IsSpendableAt(101)); // depth 100 = COINBASE_MATURITY
    }

    [Fact]
    public void Other_coins_have_no_maturity()
    {
        var coin = new Coin(Output, Height: 7, IsCoinbase: false);

        Assert.Null(coin.MaturityHeight);
        Assert.True(coin.IsSpendableAt(8));
    }
}
