using MiniBtc.Domain.Addresses;

namespace MiniBtc.Domain.Tests.Addresses;

public class Base58Tests
{
    /// <summary>A selection of Bitcoin Core's src/test/data/base58_encode_decode.json (all of it runs in py/tests).</summary>
    [Theory]
    [InlineData("", "")]
    [InlineData("61", "2g")]
    [InlineData("626262", "a3gV")]
    [InlineData("73696d706c792061206c6f6e6720737472696e67", "2cFupjhnEsSn59qHXstmK2ffpLv2")]
    [InlineData("00eb15231dfceb60925886b67d065299925915aeb172c06647", "1NS17iag9jJgTHD1VXjvLCEnZuQ3rJDE9L")]
    [InlineData("00000000000000000000", "1111111111")]
    [InlineData("000111d38e5fc9071ffcd20b4a763cc9ae4f252bb4e48fd66a835e252ada93ff480d6dd43dc62a641155a5",
        "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz")]
    [InlineData("271F359E", "zzzzy")]
    [InlineData("271F35A0", "211111")]
    public void Matches_cores_vectors(string hex, string text)
    {
        var data = Convert.FromHexString(hex);

        Assert.Equal(text, Base58.Encode(data));
        Assert.True(Base58.TryDecode(text, 256, out var decoded));
        Assert.Equal(data, decoded);
    }

    [Fact]
    public void Decoding_follows_cores_rules_for_spaces_bad_characters_and_length()
    {
        Assert.True(Base58.TryDecode(" \t\n\v\f\r skip \r\f\v\n\t ", 3, out var skipped)); // Core: outer spaces skipped
        Assert.Equal(new byte[] { 0x97, 0x1a, 0x55 }, skipped);
        Assert.False(Base58.TryDecode("3vQB 7B6MrGQZaxCuFg4oh", 100, out _)); // inner space
        Assert.False(Base58.TryDecode("invalid0", 100, out _)); // 0 is not in the alphabet
        Assert.False(Base58.TryDecode("1111", 3, out _)); // four zero bytes don't fit in three
        Assert.False(Base58.TryDecode("2g\0", 100, out _));
    }

    [Fact]
    public void Base58Check_round_trips_and_a_changed_character_fails_the_checksum()
    {
        var payload = Convert.FromHexString("6f751e76e8199196d454941c45d1b3a323f1433bd6");
        var text = Base58.EncodeCheck(payload);

        Assert.Equal("mrCDrCybB6J1vRfbwM5hemdJz73FwDBC8r", text);
        Assert.True(Base58.TryDecodeCheck(text, 21, out var decoded));
        Assert.Equal(payload, decoded);
        Assert.False(Base58.TryDecodeCheck(text[..^1] + "s", 21, out _));
        Assert.False(Base58.TryDecodeCheck(text, 20, out _)); // payload longer than allowed
    }
}
