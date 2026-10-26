namespace MiniBtc.Domain.Tests.TestData;

/// <summary>
/// Legacy-format transactions from bitcoinjs-lib's test fixtures (test/fixtures/transaction.json),
/// i.e. from an independent implementation. Their bytes HASH256 to the txids given here.
/// </summary>
internal static class LegacyTransactions
{
    /// <summary>"Standard transaction (1:1)": one P2PKH input, one P2PKH output of 100,000 sat.</summary>
    public const string StandardTxid = "a0ff943d3f644d8832b1fa74be4d0ad2577615dc28a7ef74ff8c271b603a082a";

    public const string StandardHex =
        "01000000" +                                                         // version 1
        "01" +                                                               // 1 input
        "f1fefefefefefefefefefefefefefefefefefefefefefefefefefefefefefefe" + // prevout txid, wire order
        "00000000" +                                                         // prevout index 0
        "6b" +                                                               // scriptSig: 107 bytes
        "4830450221008732a460737d956fd94d49a31890b2908f7ed7025a9c1d0f25e43290f1841716022004fa7d60" +
        "8a291d44ebbbebbadaac18f943031e7de39ef3bf9920998c43e60c0401210279be667ef9dcbbac55a06295ce" +
        "870b07029bfcdb2dce28d959f2815b16f81798" +                           //   <sig+01> <pubkey G>
        "ffffffff" +                                                         // sequence
        "01" +                                                               // 1 output
        "a086010000000000" +                                                 // 100_000 sat
        "19" +                                                               // scriptPubKey: 25 bytes
        "76a914c42e7ef92fdb603af844d064faad95db9bcdfd3d88ac" +               //   P2PKH
        "00000000";                                                          // locktime 0

    /// <summary>
    /// bitcoinjs-lib bug #367: eight outputs whose scripts are truncated push opcodes
    /// (e.g. a lone OP_PUSHDATA1, 0x4c). Still a well-formed transaction: scripts are opaque bytes.
    /// </summary>
    public const string MalformedScriptsTxid = "ebc9fa1196a59e192352d76c0f6e73167046b9d37b8302b6bb6968dfd279b767";

    public const string MalformedScriptsHex =
        "01000000019ac03d5ae6a875d970128ef9086cef276a1919684a6988023cc7254691d97e6d010000006b48304502" +
        "21009d41dc793ba24e65f571473d40b299b6459087cea1509f0d381740b1ac863cb6022039c425906fcaf51b2b84" +
        "d8092569fb3213de43abaff2180e2a799d4fcb4dd0aa012102d5ede09a8ae667d0f855ef90325e27f6ce35bbe60a" +
        "1e6e87af7f5b3c652140fdffffffff080100000000000000010101000000000000000202010100000000000000014c" +
        "0100000000000000034c02010100000000000000014d0100000000000000044dffff010100000000000000014e01" +
        "00000000000000064effffffff0100000000";
}
