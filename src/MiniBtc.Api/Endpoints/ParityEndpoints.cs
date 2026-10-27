using MiniBtc.Application.Abstractions;
using MiniBtc.Application.Parity;

namespace MiniBtc.Api.Endpoints;

public static class ParityEndpoints
{
    /// <summary>
    /// GET /parity?address=…&amp;address=… — our tip, UTXO set and the given addresses' balances
    /// against Core's getblockchaininfo, gettxoutsetinfo and scantxoutset.
    /// </summary>
    public static IEndpointRouteBuilder MapParityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/parity", async (string[]? address, ParityCheck parity, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await parity.RunAsync(address ?? [], cancellationToken));
            }
            catch (Exception ex) when (ex is BitcoinCoreRpcException or HttpRequestException)
            {
                return Results.Json(new { error = $"Bitcoin Core is not answering: {ex.Message}" }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });
        return app;
    }
}
