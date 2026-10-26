namespace MiniBtc.Domain.Tests.TestData;

/// <summary>
/// The regtest genesis block, as Bitcoin Core builds it (chainparams.cpp: CreateGenesisBlock with
/// time 1296688602, nonce 2, bits 0x207fffff). It is one header and one coinbase transaction.
/// </summary>
internal static class RegtestGenesis
{
    /// <summary>getblockhash 0 on any regtest node.</summary>
    public const string Hash = "0f9188f13cb7b2c71f2a335e3a4fc328bf5beb436012afca590b1a11466e2206";

    /// <summary>The coinbase txid, which is also the merkle root: a one-tx block's root is that tx's id.</summary>
    public const string MerkleRoot = "4a5e1e4baab89f3a32518a88c31bc87f618f76673e2cc77ab2127b7afdeda33b";

    public const string HeaderHex =
        "01000000" +                                                         // version 1
        "0000000000000000000000000000000000000000000000000000000000000000" + // prevHash: none
        "3ba3edfd7a7b12b27ac72c3e67768f617fc81bc3888a51323a9fb8aa4b1e5e4a" + // merkle root, wire order
        "dae5494d" +                                                         // time 1296688602
        "ffff7f20" +                                                         // bits 0x207fffff
        "02000000";                                                          // nonce 2

    public const string CoinbaseHex =
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

    public static byte[] Header => Convert.FromHexString(HeaderHex);
    public static byte[] Coinbase => Convert.FromHexString(CoinbaseHex);
}
