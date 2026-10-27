using Microsoft.AspNetCore.SignalR;
using MiniBtc.Api.Observability;
using MiniBtc.Api.Realtime;
using MiniBtc.Application.Broadcast;
using MiniBtc.Application.Unconfirmed;
using MiniBtc.Domain.Serialization;
using MiniBtc.Domain.Transactions;
using Patterns.DDD;

namespace MiniBtc.Api.Endpoints;

/// <summary>
/// The broadcast gateway and the mempool view. A rejection is a 400 whose "rejectedBy" says whether
/// the node refused before Core saw the transaction, or Core refused what the node had passed;
/// "code" is Core's reject reason either way. Both outcomes are logged by rule and pushed to the
/// explorer.
/// </summary>
public static class TransactionEndpoints
{
    public sealed record BroadcastRequest(string Hex);

    public static IEndpointRouteBuilder MapTransactionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/tx", async (
            BroadcastRequest request,
            TransactionBroadcaster broadcaster,
            IHubContext<ChainHub, IChainClient> hub,
            ILoggerFactory loggers,
            CancellationToken cancellationToken) =>
        {
            var logger = loggers.CreateLogger("MiniBtc.Gateway");
            var hex = request.Hex ?? "";
            var result = await broadcaster.BroadcastAsync(hex, cancellationToken);
            if (result.IsSuccess)
            {
                var txid = result.Value.Txid.ToString();
                RejectionLog.TransactionAccepted(logger, txid, result.Value.Fee);
                await hub.Clients.All.TxAccepted(new TxAcceptedEvent(txid, "gateway"));
                return Results.Ok(new { txid, fee = result.Value.Fee, unverifiedInputs = result.Value.UnverifiedInputs });
            }

            var rejection = new TxRejectedEvent(
                TxidOf(hex),
                BroadcastErrors.IsFromCore(result.Error) ? "core" : "node",
                BroadcastErrors.Reason(result.Error),
                result.Error.Message);
            RejectionLog.TransactionRejected(logger, rejection.Txid, rejection.RejectedBy, rejection.Code, rejection.Message);
            await hub.Clients.All.TxRejected(rejection);
            return Results.BadRequest(new { rejectedBy = rejection.RejectedBy, code = rejection.Code, error = rejection.Message });
        });

        app.MapGet("/mempool", (MempoolState mempool) =>
        {
            var txids = mempool.Read(view => view.Transactions.Select(tx => tx.Txid.ToString()).Order().ToList());
            return Results.Ok(new { count = txids.Count, mempoolSequence = mempool.MempoolSequence, txids });
        });

        return app;
    }

    /// <summary>The txid of the submitted bytes, for logs and the explorer, if they decode at all.</summary>
    private static string? TxidOf(string hex)
    {
        try
        {
            return Transaction.Parse(Convert.FromHexString(hex.Trim())).Txid.ToString();
        }
        catch (Exception ex) when (ex is FormatException or DecodeException)
        {
            return null;
        }
    }
}
