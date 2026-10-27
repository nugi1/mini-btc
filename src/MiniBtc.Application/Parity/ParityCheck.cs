using MiniBtc.Application.Abstractions;
using MiniBtc.Domain.Addresses;
using MiniBtc.Domain.Consensus;

namespace MiniBtc.Application.Parity;

/// <summary>
/// Two independent implementations, one chain: compares what our node computed with what Core
/// computed. Tip height and hash against Core's tip, the whole UTXO set against gettxoutsetinfo
/// (coin count and total value), and each requested address's balance against Core's own
/// scantxoutset. Green means both agree on every line; a red line is a bug in one of them.
/// </summary>
public sealed class ParityCheck(IBitcoinCoreRpc core, IUtxoStats ours, IAddressIndex addresses, NetworkParams network)
{
    public async Task<ParityReport> RunAsync(IReadOnlyCollection<string> addressTexts, CancellationToken cancellationToken = default)
    {
        var mine = await ours.GetSummaryAsync(cancellationToken);
        var theirs = await core.GetTxOutSetInfoAsync(cancellationToken);

        var checks = new List<ParityItem>
        {
            Compare("Tip height", mine.TipHeight, theirs.Height),
            Compare("Tip hash", mine.TipHash.ToString(), theirs.BestBlock.ToString()),
            Compare("UTXO count", mine.CoinCount, theirs.TxOuts),
            Compare("UTXO total (sat)", mine.TotalAmount, theirs.TotalAmount),
        };

        foreach (var text in addressTexts.Distinct())
        {
            var address = Address.Parse(text, network);
            if (address.IsFailure)
            {
                checks.Add(new ParityItem($"Balance of {text}", address.Error.Message, "not asked", Match: false));
                continue;
            }

            var coins = await addresses.GetCoinsAsync(address.Value.ScriptPubKey, cancellationToken);
            var scan = await core.ScanTxOutSetAsync($"addr({address.Value.Text})", cancellationToken);
            checks.Add(Compare($"Balance of {text} (sat)", coins.Coins.Sum(c => c.Value.Value), scan.TotalAmount));
        }

        return new ParityReport(
            DateTimeOffset.UtcNow,
            Level: mine.TipHeight == theirs.Height && mine.TipHash == theirs.BestBlock,
            Green: checks.All(c => c.Match),
            checks);
    }

    private static ParityItem Compare<T>(string name, T ours, T core) where T : IEquatable<T> =>
        new(name, ours.ToString()!, core.ToString()!, ours.Equals(core));
}

/// <param name="Level">Both tips are the same block. If not, the node is still syncing and the totals can differ for that reason alone.</param>
/// <param name="Green">Every check matches.</param>
public sealed record ParityReport(DateTimeOffset CheckedAt, bool Level, bool Green, IReadOnlyList<ParityItem> Checks);

public sealed record ParityItem(string Name, string Ours, string Core, bool Match);
