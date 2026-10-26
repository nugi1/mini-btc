using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Serialization;
using MiniBtc.Domain.Tests.TestData;
using MiniBtc.Domain.Transactions;

namespace MiniBtc.Domain.Tests.Transactions;

public class OutPointTests
{
    [Fact]
    public void Null_outpoint_is_zero_txid_and_max_index()
    {
        Assert.True(OutPoint.Null.IsNull);
        Assert.Equal(new OutPoint(UInt256.Zero, 0xFFFF_FFFF), OutPoint.Null);
        Assert.False(new OutPoint(UInt256.Zero, 0).IsNull);
    }

    [Fact]
    public void Serializes_as_36_bytes_txid_then_index()
    {
        var outPoint = new OutPoint(UInt256.Parse(RegtestGenesis.MerkleRoot), 1);

        var writer = new ByteWriter();
        outPoint.WriteTo(writer);

        Assert.Equal(
            "3ba3edfd7a7b12b27ac72c3e67768f617fc81bc3888a51323a9fb8aa4b1e5e4a" + "01000000",
            Convert.ToHexStringLower(writer.WrittenSpan));
        Assert.Equal(outPoint, OutPoint.ReadFrom(new ByteReader(writer.ToArray())));
    }

    [Fact]
    public void Prints_as_txid_colon_index()
    {
        Assert.Equal($"{RegtestGenesis.MerkleRoot}:0", new OutPoint(UInt256.Parse(RegtestGenesis.MerkleRoot), 0).ToString());
    }
}
