using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Tests.TestData;

namespace MiniBtc.Domain.Tests.Hashing;

public class UInt256Tests
{
    // The genesis hash as it sits in the next block's prevHash field.
    private const string GenesisWireHex = "06226e46111a0b59caaf126043eb5bbf28c34f3a5e332a1fc7b2b73cf188910f";

    [Fact]
    public void Display_form_is_the_wire_bytes_reversed()
    {
        var fromDisplay = UInt256.Parse(RegtestGenesis.Hash);
        var fromWire = new UInt256(Convert.FromHexString(GenesisWireHex));

        Assert.Equal(fromWire, fromDisplay);
        Assert.Equal(RegtestGenesis.Hash, fromWire.ToString());
        Assert.Equal(GenesisWireHex, Convert.ToHexStringLower(fromDisplay.ToWireBytes()));
    }

    [Fact]
    public void Zero_is_all_zero_bytes()
    {
        Assert.Equal(new string('0', 64), UInt256.Zero.ToString());
        Assert.Equal(UInt256.Zero, new UInt256(new byte[32]));
    }

    [Fact]
    public void Equality_is_by_value()
    {
        Assert.Equal(UInt256.Parse(RegtestGenesis.Hash), UInt256.Parse(RegtestGenesis.Hash));
        Assert.NotEqual(UInt256.Parse(RegtestGenesis.Hash), UInt256.Parse(RegtestGenesis.MerkleRoot));
    }

    [Fact]
    public void Rejects_wrong_lengths()
    {
        Assert.Throws<ArgumentException>(() => new UInt256(new byte[31]));
        Assert.Throws<FormatException>(() => UInt256.Parse("0f9188f1"));
        Assert.Throws<FormatException>(() => UInt256.Parse("not hex"));
    }
}
