using MiniBtc.Domain.Serialization;

namespace MiniBtc.Domain.Tests.Serialization;

public class CompactSizeTests
{
    // Every boundary where the encoding width changes, encoded the one way Core accepts.
    [Theory]
    [InlineData(0UL, "00")]
    [InlineData(252UL, "fc")]
    [InlineData(253UL, "fdfd00")]
    [InlineData(0xFFFFUL, "fdffff")]
    [InlineData(0x1_0000UL, "fe00000100")]
    [InlineData(0xFFFF_FFFFUL, "feffffffff")]
    [InlineData(0x1_0000_0000UL, "ff0000000001000000")]
    [InlineData(ulong.MaxValue, "ffffffffffffffffff")]
    public void Encodes_and_decodes_canonically(ulong value, string hex)
    {
        var writer = new ByteWriter();
        writer.WriteCompactSize(value);

        Assert.Equal(hex, Convert.ToHexStringLower(writer.WrittenSpan));
        Assert.Equal(CompactSize.SizeOf(value), writer.Length);

        var reader = new ByteReader(Convert.FromHexString(hex));
        Assert.Equal(value, reader.ReadCompactSize(rangeCheck: false));
        Assert.True(reader.IsAtEnd);
    }

    // A value written wider than needed: same number, different bytes. Core rejects these.
    [Theory]
    [InlineData("fdfc00")]             // 252 needs 1 byte, not 3
    [InlineData("fe ffff0000")]        // 65535 needs 3 bytes, not 5
    [InlineData("ff ffffffff00000000")] // 2^32-1 needs 5 bytes, not 9
    public void Rejects_non_canonical_encoding(string hex)
    {
        var reader = new ByteReader(Convert.FromHexString(hex.Replace(" ", "")));

        var ex = Assert.Throws<DecodeException>(() => reader.ReadCompactSize(rangeCheck: false));
        Assert.Equal(DecodeReasons.NonCanonicalCompactSize, ex.Reason);
    }

    [Fact]
    public void Accepts_exactly_max_size_and_rejects_one_more()
    {
        Assert.Equal(CompactSize.MaxSize, new ByteReader(Convert.FromHexString("fe00000002")).ReadCompactSize());

        var ex = Assert.Throws<DecodeException>(() => new ByteReader(Convert.FromHexString("fe01000002")).ReadCompactSize());
        Assert.Equal(DecodeReasons.CompactSizeTooLarge, ex.Reason);
    }

    [Fact]
    public void Range_check_can_be_skipped_for_non_length_fields()
    {
        var reader = new ByteReader(Convert.FromHexString("fe01000002"));
        Assert.Equal(CompactSize.MaxSize + 1, reader.ReadCompactSize(rangeCheck: false));
    }

    [Fact]
    public void Truncated_value_is_end_of_data()
    {
        var reader = new ByteReader(Convert.FromHexString("fd00")); // prefix promises 2 more bytes, has 1

        var ex = Assert.Throws<DecodeException>(() => reader.ReadCompactSize());
        Assert.Equal(DecodeReasons.EndOfData, ex.Reason);
    }

    [Fact]
    public void Var_bytes_cannot_claim_more_than_is_left()
    {
        var reader = new ByteReader(Convert.FromHexString("05aabbcc")); // length 5, only 3 bytes follow

        var ex = Assert.Throws<DecodeException>(() => reader.ReadVarBytes());
        Assert.Equal(DecodeReasons.EndOfData, ex.Reason);
    }
}
