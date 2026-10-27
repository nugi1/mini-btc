using MiniBtc.Application.Abstractions;
using MiniBtc.Domain.Addresses;
using MiniBtc.Domain.Consensus;
using MiniBtc.Domain.Transactions;
using Patterns.DDD;

namespace MiniBtc.Application.Addresses;

/// <summary>
/// What a wallet asks the node: which coins an address has, and which of them it can spend now.
/// "Now" means in the next block, at tip height + 1. That is the height Core's mempool checks
/// coinbase maturity against, so a coin reported spendable here is one Core will accept a spend of.
/// </summary>
public sealed class AddressQueries(IAddressIndex index, NetworkParams network)
{
    public async Task<Result<AddressUtxos>> GetUtxosAsync(string address, CancellationToken cancellationToken = default)
    {
        var parsed = Address.Parse(address, network);
        if (parsed.IsFailure) return parsed.Error;

        var indexed = await index.GetCoinsAsync(parsed.Value.ScriptPubKey, cancellationToken);
        var spendHeight = indexed.TipHeight + 1;
        var utxos = indexed.Coins
            .Select(entry => new AddressUtxo(
                entry.Key,
                entry.Value.Value,
                entry.Value.Height,
                Confirmations: indexed.TipHeight - entry.Value.Height + 1,
                entry.Value.IsCoinbase,
                IsSpendable: entry.Value.IsSpendableAt(spendHeight),
                entry.Value.MaturityHeight))
            .ToList();
        return new AddressUtxos(parsed.Value, indexed.TipHeight, utxos);
    }

    public async Task<Result<AddressBalance>> GetBalanceAsync(string address, CancellationToken cancellationToken = default)
    {
        var utxos = await GetUtxosAsync(address, cancellationToken);
        if (utxos.IsFailure) return utxos.Error;

        var coins = utxos.Value.Utxos;
        return new AddressBalance(
            utxos.Value.Address,
            utxos.Value.TipHeight,
            Spendable: coins.Where(c => c.IsSpendable).Sum(c => c.Value),
            Immature: coins.Where(c => !c.IsSpendable).Sum(c => c.Value),
            UtxoCount: coins.Count);
    }
}

public sealed record AddressUtxos(Address Address, int TipHeight, IReadOnlyList<AddressUtxo> Utxos);

/// <param name="MaturityHeight">For a coinbase output, the first block height that may spend it.</param>
public sealed record AddressUtxo(
    OutPoint OutPoint,
    long Value,
    int Height,
    int Confirmations,
    bool IsCoinbase,
    bool IsSpendable,
    int? MaturityHeight);

public sealed record AddressBalance(Address Address, int TipHeight, long Spendable, long Immature, int UtxoCount)
{
    public long Total => Spendable + Immature;
}
