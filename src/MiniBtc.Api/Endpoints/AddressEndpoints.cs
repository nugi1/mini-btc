using MiniBtc.Application.Addresses;
using Patterns.DDD;

namespace MiniBtc.Api.Endpoints;

/// <summary>
/// REST for wallets. Amounts are integer satoshis; txids are in display order, as RPC shows them.
/// A bad address is a 400 carrying Bitcoin Core's own error message.
/// </summary>
public static class AddressEndpoints
{
    public static IEndpointRouteBuilder MapAddressEndpoints(this IEndpointRouteBuilder app)
    {
        var addresses = app.MapGroup("/addresses/{address}");

        addresses.MapGet("/utxos", async (string address, AddressQueries queries, CancellationToken cancellationToken) =>
            (await queries.GetUtxosAsync(address, cancellationToken)).Match(
                utxos => Results.Ok(new
                {
                    address = utxos.Address.Text,
                    scriptPubKey = Convert.ToHexStringLower(utxos.Address.ScriptPubKey),
                    tipHeight = utxos.TipHeight,
                    utxos = utxos.Utxos.Select(u => new
                    {
                        txid = u.OutPoint.Txid.ToString(),
                        vout = u.OutPoint.Index,
                        value = u.Value,
                        height = u.Height,
                        confirmations = u.Confirmations,
                        coinbase = u.IsCoinbase,
                        spendable = u.IsSpendable,
                        maturityHeight = u.MaturityHeight,
                    }),
                }),
                BadRequest));

        addresses.MapGet("/balance", async (string address, AddressQueries queries, CancellationToken cancellationToken) =>
            (await queries.GetBalanceAsync(address, cancellationToken)).Match(
                balance => Results.Ok(new
                {
                    address = balance.Address.Text,
                    tipHeight = balance.TipHeight,
                    spendable = balance.Spendable,
                    immature = balance.Immature,
                    total = balance.Total,
                    utxoCount = balance.UtxoCount,
                }),
                BadRequest));

        return app;
    }

    private static IResult BadRequest(Error error) => Results.BadRequest(new { code = error.Code, error = error.Message });
}
