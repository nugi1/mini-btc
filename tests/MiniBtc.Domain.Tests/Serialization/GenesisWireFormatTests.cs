using System.Text;
using MiniBtc.Domain.Serialization;

namespace MiniBtc.Domain.Tests.Serialization;

/// <summary>
/// Reads and re-writes the regtest genesis block's header and coinbase field by field with
/// ByteReader/ByteWriter. The bytes are real: the header double-SHA256es to the regtest genesis
/// hash 0f9188f1…466e2206 and the coinbase to the merkle root 4a5e1e4b…deda33b (checked in E1's
/// hashing commit). Here we prove every field sits where the wire format says and round-trips.
/// </summary>
public class GenesisWireFormatTests
{
    private const string Header =
        "01000000" +                                                         // version 1
        "0000000000000000000000000000000000000000000000000000000000000000" + // prevHash: none
        "3ba3edfd7a7b12b27ac72c3e67768f617fc81bc3888a51323a9fb8aa4b1e5e4a" + // merkle root, wire order
        "dae5494d" +                                                         // time 1296688602
        "ffff7f20" +                                                         // bits 0x207fffff
        "02000000";                                                          // nonce 2

    private const string Coinbase =
        "01000000" +                                                         // version 1
        "01" +                                                               // 1 input
        "0000000000000000000000000000000000000000000000000000000000000000" + // prevout txid: null
        "ffffffff" +                                                         // prevout index: null
        "4d" +                                                               // scriptSig: 77 bytes
        "04ffff001d0104455468652054696d65732030332f4a616e2f32303039204368616e63656c6c6f72206f6e20" +
        "6272696e6b206f66207365636f6e64206261696c6f757420666f722062616e6b73" +
        "ffffffff" +                                                         // sequence
        "01" +                                                               // 1 output
        "00f2052a01000000" +                                                 // 5_000_000_000 sat
        "43" +                                                               // scriptPubKey: 67 bytes
        "4104678afdb0fe5548271967f1a67130b7105cd6a828e03909a67962e0ea1f61deb649f6bc3f4cef38c4f355" +
        "04e51ec112de5c384df7ba0b8d578a4c702b6bf11d5fac" +
        "00000000";                                                          // locktime 0

    [Fact]
    public void Header_fields_are_little_endian_and_round_trip()
    {
        var bytes = Convert.FromHexString(Header);
        var r = new ByteReader(bytes);

        var version = r.ReadInt32();
        var prevHash = r.ReadBytes(32);
        var merkleRoot = r.ReadBytes(32);
        var time = r.ReadUInt32();
        var bits = r.ReadUInt32();
        var nonce = r.ReadUInt32();

        Assert.Equal(80, r.Position);
        Assert.True(r.IsAtEnd);
        Assert.Equal(1, version);
        Assert.All(prevHash, b => Assert.Equal(0, b));
        Assert.Equal(1296688602u, time);
        Assert.Equal(0x207fffffu, bits);
        Assert.Equal(2u, nonce);

        // Hashes are stored in wire order and displayed reversed (RPC, explorers).
        // (Not merkleRoot.Reverse(): in C# 14 that binds to the in-place Span.Reverse.)
        var display = (byte[])merkleRoot.Clone();
        Array.Reverse(display);
        Assert.Equal(
            "4a5e1e4baab89f3a32518a88c31bc87f618f76673e2cc77ab2127b7afdeda33b",
            Convert.ToHexStringLower(display));

        var w = new ByteWriter();
        w.WriteInt32(version);
        w.WriteBytes(prevHash);
        w.WriteBytes(merkleRoot);
        w.WriteUInt32(time);
        w.WriteUInt32(bits);
        w.WriteUInt32(nonce);
        Assert.Equal(bytes, w.ToArray());
    }

    [Fact]
    public void Coinbase_fields_parse_and_round_trip_byte_exact()
    {
        var bytes = Convert.FromHexString(Coinbase);
        var r = new ByteReader(bytes);

        var version = r.ReadInt32();
        var inputCount = r.ReadCount();
        var prevTxid = r.ReadBytes(32);
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
        Assert.All(prevTxid, b => Assert.Equal(0, b));
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
        w.WriteBytes(prevTxid);
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
