using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Scripts;

namespace MiniBtc.Domain.Tests.Scripts;

public class OutputScriptsTests
{
    /// <summary>
    /// Bitcoin Core's src/test/data/key_io_valid.json, regtest entries (also in py/tests/data), plus the
    /// P2WPKH output of the captured "mixed" block with the address Core's getblock printed for it.
    /// </summary>
    [Theory]
    [InlineData("76a914fdeca3b08e38af53d7c4c60e3ad208ce5066441088ac", "pubkeyhash", "n4fajahJrAuKbN7uNsKjLjQkz9Qn5ewJXQ")]
    [InlineData("a91436e9f191e0b75036a77f65e2eaa4752443233fbe87", "scripthash", "2MxFajLApXpYk4VodBSZSt7rw8y4ryABkfA")]
    [InlineData("00146f58bac9506f9bfb821d62e69330410bb56f0085", "witness_v0_keyhash", "bcrt1qdavt4j2sd7dlhqsavtnfxvzppw6k7qy97tmnu9")]
    [InlineData("0020ecce89afb8f0bdbcab325d6e11f2aaeb549f50a9d91fb8e64c814faa685367b2", "witness_v0_scripthash",
        "bcrt1qan8gntac7z7me2ejt4hpru42ad2f759fmy0m3ejvs98656znv7eqga4uhv")]
    [InlineData("51204b8d20316baaf061adbfe72c9d914d678cd5004d49356ec9949ba752777171ac", "witness_v1_taproot",
        "bcrt1pfwxjqvtt4tcxrtdluukfmy2dv7xd2qzdfy6kajv5nwn4yam3wxkq3553uh")]
    [InlineData("6028368279cbe6f5cbbc2ba8154883a9a29e5517d1f3c03cac4f39ce3225060b3efb799cd9c412746ae2", "witness_unknown",
        "bcrt1sx6p8njlx7h9mc2agz4yg82dzne23050ncq72cneeecez2pst8mahn8xecsf8g6hzx94420")]
    [InlineData("0014a51e10ca66e4e4b7c7347ca4001ad38d7912711e", "witness_v0_keyhash", "bcrt1q550ppjnxunjt03e50jjqqxkn34u3yug730el6g")]
    public void Classifies_and_spells_addresses_as_core_does(string scriptHex, string type, string address)
    {
        var script = Convert.FromHexString(scriptHex);

        Assert.Equal(type, OutputScripts.CoreName(OutputScripts.Classify(script)));
        Assert.Equal(address, OutputScripts.ToAddress(script, NetworkParams.Regtest));
    }

    [Theory]
    [InlineData("6a24aa21a9edc2287607a95168e48775e84ec5769ff8cc3d8d39fd54182736280c8d8ad057b0", "nulldata")] // witness commitment
    [InlineData("6a", "nulldata")]
    [InlineData("6aac", "nonstandard")] // OP_RETURN followed by a non-push
    [InlineData("4104678afdb0fe5548271967f1a67130b7105cd6a828e03909a67962e0ea1f61deb649f6bc3f4cef38c4f35504e51ec112de5c384df7ba0b8d578a4c702b6bf11d5fac", "pubkey")] // genesis
    [InlineData("51024e73", "anchor")]
    [InlineData("51", "nonstandard")]
    [InlineData("001300000000000000000000000000000000000000", "nonstandard")] // v0 with a 19-byte program
    public void Recognizes_the_other_templates_which_have_no_address_or_a_special_one(string scriptHex, string type)
    {
        var script = Convert.FromHexString(scriptHex);

        Assert.Equal(type, OutputScripts.CoreName(OutputScripts.Classify(script)));
        if (type is "nulldata" or "nonstandard" or "pubkey")
            Assert.Null(OutputScripts.ToAddress(script, NetworkParams.Regtest));
    }

    [Fact]
    public void A_one_of_two_bare_multisig_is_multisig()
    {
        var key = "21" + "02" + new string('1', 64);
        var script = Convert.FromHexString("51" + key + key + "52ae");

        Assert.Equal(OutputType.Multisig, OutputScripts.Classify(script));
    }
}
