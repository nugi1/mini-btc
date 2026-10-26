using MiniBtc.Application.Abstractions;
using MiniBtc.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddBitcoinCoreRpc(builder.Configuration);

var app = builder.Build();

// Liveness plus a live check of the Core connection: which chain, and Core's tip.
app.MapGet("/health", async (IBitcoinCoreRpc core, CancellationToken cancellationToken) =>
{
    try
    {
        var info = await core.GetBlockchainInfoAsync(cancellationToken);
        return Results.Ok(new
        {
            status = "ok",
            core = new { chain = info.Chain, height = info.Blocks, tip = info.BestBlockHash.ToString() },
        });
    }
    catch (Exception ex) when (ex is BitcoinCoreRpcException or HttpRequestException)
    {
        return Results.Json(new { status = "degraded", core = ex.Message }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.Run();
