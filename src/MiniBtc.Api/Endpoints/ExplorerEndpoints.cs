using MiniBtc.Application.Explorer;

namespace MiniBtc.Api.Endpoints;

/// <summary>
/// Read-only JSON for the explorer page: everything is the node's own view of the chain, decoded
/// with its own code. Amounts are satoshis, hashes are display order.
/// </summary>
public static class ExplorerEndpoints
{
    public static IEndpointRouteBuilder MapExplorerEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/chain/summary", async (ExplorerQueries queries, CancellationToken cancellationToken) =>
            Results.Ok(await queries.GetSummaryAsync(cancellationToken)));

        app.MapGet("/blocks", async (int? limit, int? before, ExplorerQueries queries, CancellationToken cancellationToken) =>
            Results.Ok(await queries.GetLatestBlocksAsync(limit ?? 20, before, cancellationToken)));

        app.MapGet("/blocks/{id}", async (string id, ExplorerQueries queries, CancellationToken cancellationToken) =>
            await queries.GetBlockAsync(id, cancellationToken) is { } block
                ? Results.Ok(block)
                : Results.NotFound(new { error = $"No block {id} in the node's active chain" }));

        app.MapGet("/tx/{txid}", async (string txid, ExplorerQueries queries, CancellationToken cancellationToken) =>
            await queries.GetTransactionAsync(txid, cancellationToken) is { } tx
                ? Results.Ok(tx)
                : Results.NotFound(new { error = $"No transaction {txid} in the node's chain or mempool view" }));

        app.MapGet("/mempool/entries", async (ExplorerQueries queries, CancellationToken cancellationToken) =>
            Results.Ok(await queries.GetMempoolAsync(cancellationToken)));

        return app;
    }
}
