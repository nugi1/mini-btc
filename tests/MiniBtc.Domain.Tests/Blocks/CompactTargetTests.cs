using MiniBtc.Domain.Blocks;
using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Tests.TestData;

namespace MiniBtc.Domain.Tests.Blocks;

public class CompactTargetTests
{
    // Bitcoin Core v30.0, src/test/arith_uint256_tests.cpp, bignum_SetCompact: compact in,
    // decoded value (GetHex, leading zeros dropped here), the two flags, and GetCompact back out.
    [Theory]
    [InlineData(0x00000000U, "0", false, false, 0x00000000U)]
    [InlineData(0x00123456U, "0", false, false, 0x00000000U)]
    [InlineData(0x01003456U, "0", false, false, 0x00000000U)]
    [InlineData(0x02000056U, "0", false, false, 0x00000000U)]
    [InlineData(0x03000000U, "0", false, false, 0x00000000U)]
    [InlineData(0x04000000U, "0", false, false, 0x00000000U)]
    [InlineData(0x00923456U, "0", false, false, 0x00000000U)]
    [InlineData(0x01803456U, "0", false, false, 0x00000000U)]
    [InlineData(0x02800056U, "0", false, false, 0x00000000U)]
    [InlineData(0x03800000U, "0", false, false, 0x00000000U)]
    [InlineData(0x04800000U, "0", false, false, 0x00000000U)]
    [InlineData(0x01123456U, "12", false, false, 0x01120000U)]
    [InlineData(0x01fedcbaU, "7e", true, false, 0x01fe0000U)]
    [InlineData(0x02123456U, "1234", false, false, 0x02123400U)]
    [InlineData(0x03123456U, "123456", false, false, 0x03123456U)]
    [InlineData(0x04123456U, "12345600", false, false, 0x04123456U)]
    [InlineData(0x04923456U, "12345600", true, false, 0x04923456U)]
    [InlineData(0x05009234U, "92340000", false, false, 0x05009234U)]
    [InlineData(0x20123456U, "1234560000000000000000000000000000000000000000000000000000000000", false, false, 0x20123456U)]
    public void Matches_core_set_and_get_compact(uint bits, string hex, bool negative, bool overflow, uint encoded)
    {
        var target = CompactTarget.Decode(bits, out var isNegative, out var isOverflow);

        Assert.Equal(hex.PadLeft(64, '0'), target.ToString());
        Assert.Equal(negative, isNegative);
        Assert.Equal(overflow, isOverflow);
        Assert.Equal(encoded, CompactTarget.Encode(target, negative));
    }

    [Fact]
    public void Size_beyond_32_bytes_overflows()
    {
        CompactTarget.Decode(0xff123456, out var negative, out var overflow);

        Assert.False(negative);
        Assert.True(overflow);
    }

    [Fact]
    public void Never_encodes_with_the_sign_bit_set()
    {
        // 0x80 as "01 800000" would read back as negative, so it becomes "02 008000".
        Assert.Equal(0x02008000U, CompactTarget.Encode(UInt256.FromBigInteger(0x80)));
    }

    [Fact]
    public void Regtest_bits_are_the_compact_form_of_the_pow_limit()
    {
        var target = CompactTarget.Decode(RegtestGenesis.Bits, out _, out _);

        Assert.Equal("7fffff" + new string('0', 58), target.ToString());
        Assert.Equal(RegtestGenesis.Bits, CompactTarget.Encode(UInt256.Parse(RegtestGenesis.PowLimit)));
        Assert.True(target <= UInt256.Parse(RegtestGenesis.PowLimit)); // compact form rounds down
    }

    [Fact]
    public void Mainnet_genesis_bits_show_the_sign_bit_shift()
    {
        // Mainnet powLimit 00000000ffff…ff: its top three bytes are ff ff ff, which has the sign bit,
        // so GetCompact shifts to 00 ff ff and grows the size from 28 to 29: 0x1d00ffff.
        var powLimit = UInt256.Parse("00000000" + new string('f', 56));

        Assert.Equal(0x1d00ffffU, CompactTarget.Encode(powLimit));
        Assert.Equal("00000000ffff" + new string('0', 52), CompactTarget.Decode(0x1d00ffff, out _, out _).ToString());
    }
}
