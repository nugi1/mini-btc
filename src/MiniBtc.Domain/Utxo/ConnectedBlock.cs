using MiniBtc.Domain.Hashing;
using MiniBtc.Domain.Transactions;

namespace MiniBtc.Domain.Utxo;

/// <summary>What connecting a block produced.</summary>
/// <param name="Undo">The coins it spent, for disconnecting it again.</param>
/// <param name="Changes">How the UTXO set changed, for persisting the change instead of the whole set.</param>
/// <param name="Fees">The fees its transactions paid in total.</param>
/// <param name="UnverifiedScripts">Inputs whose scripts our code doesn't verify (not P2PKH, or not a
/// standard P2PKH spend); they were accepted on Core's word.</param>
public sealed record ConnectedBlock(BlockUndo Undo, UtxoChanges Changes, long Fees, IReadOnlyList<UnverifiedScript> UnverifiedScripts);

/// <summary>
/// The net change a block made to the UTXO set. A coin created and spent inside the same block
/// appears in neither list: the set never held it.
/// </summary>
/// <param name="Spent">Coins removed from the set as it stood before the block.</param>
/// <param name="Created">Coins added by the block and still unspent after it.</param>
public sealed record UtxoChanges(IReadOnlyList<OutPoint> Spent, IReadOnlyList<KeyValuePair<OutPoint, Coin>> Created);

/// <summary>One input accepted without script verification, and why.</summary>
public sealed record UnverifiedScript(UInt256 Txid, int InputIndex, string Reason);
