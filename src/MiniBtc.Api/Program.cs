using MiniBtc.Application.Abstractions;
using MiniBtc.Application.ChainSync;
using MiniBtc.Domain.Consensus;
using MiniBtc.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddBitcoinCoreRpc(builder.Configuration);
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddSingleton(NetworkParams.Regtest);
builder.Services.AddSingleton(builder.Configuration.GetSection(ChainSyncOptions.SectionName).Get<ChainSyncOptions>() ?? new ChainSyncOptions());
builder.Services.AddScoped<ChainSynchronizer>();

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
