using MiniBtc.Domain.Scripts;

namespace MiniBtc.Domain.Tests.Scripts;

public class DerSignatureTests
{
    // A real signature (Core's wallet, "mixed" block) with its sighash byte.
    private const string Valid =
        "30440220460e28f3dfda0572e3089a24a725e4ec04c4f68eeb476526177f989811ca982b02203476ec250cf516ceb9e2da73089cc9f8b70d7cb6c30b56815984ac691276bfdf01";

    [Fact]
    public void A_real_signature_is_strict_der() => Assert.True(DerSignature.IsValidEncoding(Convert.FromHexString(Valid)));

    [Theory]
    [InlineData("3006020101020101")]          // 8 bytes: shorter than any valid signature
    [InlineData("3106020101020101" + "01")]   // not a SEQUENCE (0x31)
    [InlineData("3007020101020101" + "01")]   // total length off by one
    [InlineData("3006030101020101" + "01")]   // R is not an INTEGER
    [InlineData("3006020181020101" + "01")]   // R negative (top bit set)
    [InlineData("300702020001020101" + "01")] // R padded with an unneeded 0x00
    [InlineData("3006020101020181" + "01")]   // S negative
    [InlineData("300602000202010101")]        // zero-length R (lengths otherwise consistent)
    public void Sloppy_encodings_are_rejected(string hex) =>
        Assert.False(DerSignature.IsValidEncoding(Convert.FromHexString(hex)));

    [Fact]
    public void A_0x00_pad_is_required_when_the_top_bit_would_be_set()
    {
        Assert.True(DerSignature.IsValidEncoding(Convert.FromHexString("30070202008102010101")));  // R = 0x0081
    }

    [Fact]
    public void Parse_returns_r_and_s()
    {
        var (r, s) = DerSignature.Parse(Convert.FromHexString(Valid)[..^1]);

        Assert.Equal("460e28f3dfda0572e3089a24a725e4ec04c4f68eeb476526177f989811ca982b", Convert.ToHexStringLower(r));
        Assert.Equal("3476ec250cf516ceb9e2da73089cc9f8b70d7cb6c30b56815984ac691276bfdf", Convert.ToHexStringLower(s));
    }
}
