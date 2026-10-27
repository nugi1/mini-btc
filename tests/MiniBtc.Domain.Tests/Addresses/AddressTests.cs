using MiniBtc.Domain.Addresses;
using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Tests.TestData;

namespace MiniBtc.Domain.Tests.Addresses;

public class AddressTests
{
    private static readonly NetworkParams Regtest = NetworkParams.Regtest;

    [Fact]
    public void Key_ones_address_is_the_one_the_python_wallet_derives()
    {
        var address = Address.FromPubKeyHash(Hashes.Hash160(TestSigner.KeyOne.PublicKey), Regtest);

        Assert.Equal("mrCDrCybB6J1vRfbwM5hemdJz73FwDBC8r", address.Text);
        Assert.Equal(TestSigner.KeyOne.P2pkhScript, address.ScriptPubKey);
    }

    /// <summary>Bitcoin Core's src/test/data/key_io_valid.json, the regtest Base58 entries.</summary>
    [Theory]
    [InlineData("n4fajahJrAuKbN7uNsKjLjQkz9Qn5ewJXQ", "76a914fdeca3b08e38af53d7c4c60e3ad208ce5066441088ac", AddressType.P2pkh)]
    [InlineData("mfhE6jAUwjUDNZhaX1PAsDTKfneQF2Nshc", "76a91401f15a4cc063dae4f4d56b89bfbc8bcc9ae5387c88ac", AddressType.P2pkh)]
    [InlineData("2MxFajLApXpYk4VodBSZSt7rw8y4ryABkfA", "a91436e9f191e0b75036a77f65e2eaa4752443233fbe87", AddressType.P2sh)]
    [InlineData("2MxNm1VHyVU4RuP3u1c1v5aQLk2dQjwy1Qk", "a91438456f7c076356abadcc67b92ad777eb20fb9f8887", AddressType.P2sh)]
    public void Cores_regtest_addresses_decode_to_their_scripts(string text, string scriptHex, AddressType type)
    {
        var address = Address.Parse(text, Regtest);

        Assert.True(address.IsSuccess, address.Error?.ToString());
        Assert.Equal(type, address.Value.Type);
        Assert.Equal(scriptHex, Convert.ToHexStringLower(address.Value.ScriptPubKey));
    }

    [Fact]
    public void Errors_carry_cores_messages()
    {
        var mainnetKeyOne = Base58.EncodeCheck(Convert.FromHexString("00751e76e8199196d454941c45d1b3a323f1433bd6"));
        var tooLong = Base58.EncodeCheck(new byte[] { 0x6f }.Concat(new byte[21]).ToArray());
        var tooShort = Base58.EncodeCheck(new byte[] { 0x6f }.Concat(new byte[19]).ToArray());

        AssertRejected(mainnetKeyOne, AddressErrors.UnsupportedBase58);
        AssertRejected("mrCDrCybB6J1vRfbwM5hemdJz73FwDBC8s", AddressErrors.InvalidChecksumOrLength);
        AssertRejected(tooLong, AddressErrors.InvalidChecksumOrLength);
        AssertRejected(tooShort, AddressErrors.InvalidLength);
        AssertRejected("mrCDrCybB6J1vRfbwM5hemdJz73FwDBC80", AddressErrors.InvalidEncoding);
        AssertRejected("", AddressErrors.InvalidChecksumOrLength);
        AssertRejected("bcrt1q550ppjnxunjt03e50jjqqxkn34u3yug730el6g", AddressErrors.Bech32NotSupported);
    }

    private static void AssertRejected(string text, string message)
    {
        var result = Address.Parse(text, Regtest);

        Assert.True(result.IsFailure, $"{text} parsed");
        Assert.Equal(AddressErrors.Code, result.Error!.Code);
        Assert.Equal(message, result.Error!.Message);
    }
}
