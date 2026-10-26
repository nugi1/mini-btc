namespace MiniBtc.Domain.Tests.TestData;

/// <summary>
/// SegWit transactions from bitcoinjs-lib's test fixtures (test/fixtures/transaction.json):
/// "whex" is the extended serialization and "hex" the same tx without witnesses. The txids are
/// bitcoinjs's. The wtxids are HASH256 of the extended bytes, computed with Python's hashlib;
/// E1's integration test checks wtxids against Core's own getrawtransaction "hash" field.
/// </summary>
internal static class WitnessTransactions
{
    /// <summary>Native P2WPKH spend ("P2WKH"): empty scriptSig, witness = &lt;signature&gt; &lt;compressed pubkey&gt;.</summary>
    public const string P2wpkhTxid = "10bbdf7e9301bc21f1da8684f25fd66bc1314904c30da25a8ebf967c58c89269";
    public const string P2wpkhWtxid = "911c97abc525c0424c7db48126ed14a65308f467e1a38449128c3878bc586f2d";

    public const string P2wpkhHex =
        "0100000000010133defbe3e28860007ff3e21222774c220cb35d554fa3e3796d25bf8ee983e1080000000000ff" +
        "ffffff0160ea0000000000001976a914851a33a5ef0d4279bd5854949174e2c65b1d450088ac02483045022100" +
        "97c3006f0b390982eb47f762b2853773c6cedf83668a22d710f4c13c4fd6b15502205e26ef16a81fc818a37f3a" +
        "34fc6d0700e61100ea6c6773907c9c046042c440340121038de63cf582d058a399a176825c045672d5ff8ea25b" +
        "64d28d4375dcdb14c02b2b00000000";

    public const string P2wpkhStrippedHex =
        "010000000133defbe3e28860007ff3e21222774c220cb35d554fa3e3796d25bf8ee983e1080000000000ffffff" +
        "ff0160ea0000000000001976a914851a33a5ef0d4279bd5854949174e2c65b1d450088ac00000000";

    /// <summary>Coinbase with a witness, the shape Core's block assembler produces: BIP34 height in the
    /// scriptSig, a 32-byte zero witness reserved value, and an OP_RETURN aa21a9ed witness commitment.</summary>
    public const string CoinbaseTxid = "c881f7b084a367b0603abbcb9c5c639318e6166770e3f9b27a1ee3f8b6a16517";
    public const string CoinbaseWtxid = "b3a295ad426c1d2bb818697e6092308b224721d0f0c094b3c9846c15f5e46d42";

    public const string CoinbaseHex =
        "020000000001010000000000000000000000000000000000000000000000000000000000000000ffffffff0502" +
        "2b020101ffffffff02c0cf402500000000232103c6c5964853fd00fb3271ac002831c66825102d223c706ce0ee" +
        "99e73db3be4aa1ac0000000000000000266a24aa21a9edff828eb21f40ab251d9f107792670aba9299028b894a" +
        "364fda570f6a089dcfe90120000000000000000000000000000000000000000000000000000000000000000000" +
        "000000";

    public const string CoinbaseStrippedHex =
        "02000000010000000000000000000000000000000000000000000000000000000000000000ffffffff05022b02" +
        "0101ffffffff02c0cf402500000000232103c6c5964853fd00fb3271ac002831c66825102d223c706ce0ee99e7" +
        "3db3be4aa1ac0000000000000000266a24aa21a9edff828eb21f40ab251d9f107792670aba9299028b894a364f" +
        "da570f6a089dcfe900000000";
}
