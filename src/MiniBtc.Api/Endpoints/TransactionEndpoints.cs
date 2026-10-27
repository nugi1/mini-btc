using MiniBtc.Application.Broadcast;
using MiniBtc.Application.Unconfirmed;
using Patterns.DDD;

namespace MiniBtc.Api.Endpoints;

/// <summary>
/// The broadcast gateway and the mempool view. A rejection is a 400 whose "rejectedBy" says whether
/// the node refused before Core saw the transaction, or Core refused what the node had passed;
/// "code" is Core's reject reason either way.
/// </summary>
public static class TransactionEndpoints
{
    public sealed record BroadcastRequest(string Hex);

    public static IEndpointRouteBuilder MapTransactionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/tx", async (BroadcastRequest request, TransactionBroadcaster broadcaster, CancellationToken cancellationToken) =>
            (await broadcaster.BroadcastAsync(request.Hex ?? "", cancellationToken)).Match(
                receipt => Results.Ok(new { txid = receipt.Txid.ToString(), fee = receipt.Fee, unverifiedInputs = receipt.UnverifiedInputs }),
                Rejected));

        app.MapGet("/mempool", (MempoolState mempool) =>
        {
            var txids = mempool.Read(view => view.Transactions.Select(tx => tx.Txid.ToString()).Order().ToList());
            return Results.Ok(new { count = txids.Count, mempoolSequence = mempool.MempoolSequence, txids });
        });

        return app;
    }

    private static IResult Rejected(Error error) => Results.BadRequest(new
    {
        rejectedBy = BroadcastErrors.IsFromCore(error) ? "core" : "node",
        code = BroadcastErrors.Reason(error),
        error = error.Message,
    });
}
