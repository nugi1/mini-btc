using MiniBtc.Domain.Hashing;

namespace MiniBtc.Domain.Utxo;

/// <summary>What connecting a block produced.</summary>
/// <param name="Undo">The coins it spent, for disconnecting it again.</param>
/// <param name="Fees">The fees its transactions paid in total.</param>
/// <param name="UnverifiedScripts">Inputs whose scripts our code doesn't verify (not P2PKH, or not a
/// standard P2PKH spend); they were accepted on Core's word.</param>
public sealed record ConnectedBlock(BlockUndo Undo, long Fees, IReadOnlyList<UnverifiedScript> UnverifiedScripts);

/// <summary>One input accepted without script verification, and why.</summary>
public sealed record UnverifiedScript(UInt256 Txid, int InputIndex, string Reason);
