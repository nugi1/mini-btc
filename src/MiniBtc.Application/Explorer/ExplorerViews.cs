namespace MiniBtc.Application.Explorer;

// What the explorer shows. Plain strings and numbers only: hashes in display order, amounts in
// satoshis, so any client can render them without Bitcoin types.

public sealed record ChainSummary(
    int TipHeight,
    string TipHash,
    DateTimeOffset TipTime,
    long UtxoCount,
    long UtxoTotal,
    int MempoolCount,
    int? CoreHeight,
    string Network);

public sealed record BlockRow(string Hash, int Height, DateTimeOffset Time, int TransactionCount, int Size, long Fees);

public sealed record BlockView(
    string Hash,
    int Height,
    int Confirmations,
    string PreviousHash,
    string? NextHash,
    string MerkleRoot,
    DateTimeOffset Time,
    string Version,
    string Bits,
    uint Nonce,
    int Size,
    int Weight,
    long Fees,
    long Subsidy,
    IReadOnlyList<TxRow> Transactions);

/// <param name="Fee">Null for a coinbase, or when an input's value is unknown.</param>
public sealed record TxRow(string Txid, bool Coinbase, int Inputs, int Outputs, long TotalOut, long? Fee, int Vsize);

public sealed record TxView(
    string Txid,
    string Wtxid,
    string Status,
    string? BlockHash,
    int? BlockHeight,
    int? Confirmations,
    uint Version,
    uint LockTime,
    int Size,
    int Vsize,
    int Weight,
    bool Coinbase,
    long TotalIn,
    long TotalOut,
    long? Fee,
    IReadOnlyList<TxInputView> Inputs,
    IReadOnlyList<TxOutputView> Outputs,
    string Hex);

/// <param name="Value">Satoshis of the coin spent; null for a coinbase or if unknown.</param>
public sealed record TxInputView(
    int Index,
    bool Coinbase,
    string? PrevTxid,
    uint? PrevVout,
    long? Value,
    string? Address,
    string? Type,
    string ScriptSig,
    IReadOnlyList<string> ScriptSigPushes,
    IReadOnlyList<string> Witness,
    uint Sequence);

/// <param name="Spent">True/false for confirmed outputs; null for unspendable outputs or unconfirmed transactions.</param>
public sealed record TxOutputView(int Index, long Value, string ScriptPubKey, string Type, string? Address, bool? Spent);

public sealed record MempoolRow(string Txid, int Vsize, int Inputs, int Outputs, long TotalOut, long? Fee);
