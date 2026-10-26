using System.Text;
using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Serialization;
using MiniBtc.Domain.Tests.TestData;

namespace MiniBtc.Domain.Tests.Serialization;

/// <summary>
/// Reads and re-writes the regtest genesis header and coinbase field by field with
/// ByteReader/ByteWriter. HashesTests proves these are the real bytes (they hash to the genesis
/// hash and the merkle root). Here we prove every field sits where the wire format says.
/// </summary>
public class GenesisWireFormatTests
{
    [Fact]
    public void Header_fields_are_little_endian_and_round_trip()
    {
        var bytes = RegtestGenesis.Header;
        var r = new ByteReader(bytes);

        var version = r.ReadInt32();
        var prevHash = r.ReadUInt256();
        var merkleRoot = r.ReadUInt256();
        var time = r.ReadUInt32();
        var bits = r.ReadUInt32();
        var nonce = r.ReadUInt32();

        Assert.Equal(80, r.Position);
        Assert.True(r.IsAtEnd);
        Assert.Equal(1, version);
        Assert.Equal(UInt256.Zero, prevHash);
        Assert.Equal(RegtestGenesis.MerkleRoot, merkleRoot.ToString()); // display = wire reversed
        Assert.Equal(1296688602u, time);
        Assert.Equal(0x207fffffu, bits);
        Assert.Equal(2u, nonce);

        var w = new ByteWriter();
        w.WriteInt32(version);
        w.WriteUInt256(prevHash);
        w.WriteUInt256(merkleRoot);
        w.WriteUInt32(time);
        w.WriteUInt32(bits);
        w.WriteUInt32(nonce);
        Assert.Equal(bytes, w.ToArray());
    }

    [Fact]
    public void Coinbase_fields_parse_and_round_trip_byte_exact()
    {
        var bytes = RegtestGenesis.Coinbase;
        var r = new ByteReader(bytes);

        var version = r.ReadInt32();
        var inputCount = r.ReadCount();
        var prevTxid = r.ReadUInt256();
        var prevIndex = r.ReadUInt32();
        var scriptSig = r.ReadVarBytes();
        var sequence = r.ReadUInt32();
        var outputCount = r.ReadCount();
        var value = r.ReadInt64();
        var scriptPubKey = r.ReadVarBytes();
        var lockTime = r.ReadUInt32();

        Assert.True(r.IsAtEnd);
        Assert.Equal(204, bytes.Length);
        Assert.Equal(1, version);
        Assert.Equal(1, inputCount);
        Assert.Equal(UInt256.Zero, prevTxid);                // coinbase: null prevout
        Assert.Equal(uint.MaxValue, prevIndex);
        Assert.Equal(77, scriptSig.Length);
        // push 4 bytes (ffff001d), push 1 byte (04), push 69 bytes: the headline.
        Assert.Equal(
            "The Times 03/Jan/2009 Chancellor on brink of second bailout for banks",
            Encoding.ASCII.GetString(scriptSig, 8, 69));
        Assert.Equal(uint.MaxValue, sequence);
        Assert.Equal(1, outputCount);
        Assert.Equal(5_000_000_000L, value);
        Assert.Equal(67, scriptPubKey.Length);
        Assert.Equal(0u, lockTime);

        var w = new ByteWriter();
        w.WriteInt32(version);
        w.WriteCompactSize((ulong)inputCount);
        w.WriteUInt256(prevTxid);
        w.WriteUInt32(prevIndex);
        w.WriteVarBytes(scriptSig);
        w.WriteUInt32(sequence);
        w.WriteCompactSize((ulong)outputCount);
        w.WriteInt64(value);
        w.WriteVarBytes(scriptPubKey);
        w.WriteUInt32(lockTime);
        Assert.Equal(bytes, w.ToArray());
    }
}
