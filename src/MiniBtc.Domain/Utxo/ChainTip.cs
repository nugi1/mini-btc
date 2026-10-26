using MiniBtc.Domain.Hashing;

namespace MiniBtc.Domain.Utxo;

/// <summary>The block the UTXO set currently reflects: the result of applying every block up to it.</summary>
public readonly record struct ChainTip(UInt256 Hash, int Height);
