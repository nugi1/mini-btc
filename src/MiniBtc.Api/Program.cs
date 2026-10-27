using MassTransit;
using MiniBtc.Api.Endpoints;
using MiniBtc.Api.Hosting;
using MiniBtc.Application.Abstractions;
using MiniBtc.Application.Addresses;
using MiniBtc.Application.Broadcast;
using MiniBtc.Application.ChainSync;
using MiniBtc.Application.Unconfirmed;
using MiniBtc.Domain.Consensus;
using MiniBtc.Infrastructure;
using MiniBtc.Infrastructure.Messaging;
using MiniBtc.Infrastructure.Zmq;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddBitcoinCoreRpc(builder.Configuration);
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddMessaging(builder.Configuration, x =>
{
    x.AddConsumer<BlockConnectedLogConsumer>();
    x.AddConsumer<BlockAnnouncedConsumer>();
});
builder.Services.AddZmqBlockNotifications(builder.Configuration);
builder.Services.AddSingleton(NetworkParams.Regtest);
builder.Services.AddSingleton(builder.Configuration.GetSection(ChainSyncOptions.SectionName).Get<ChainSyncOptions>() ?? new ChainSyncOptions());
builder.Services.AddScoped<ChainSynchronizer>();
builder.Services.AddScoped<AddressQueries>();
builder.Services.AddSingleton<MempoolState>();
builder.Services.AddScoped<TransactionBroadcaster>();
builder.Services.AddSingleton(builder.Configuration.GetSection(ChainSyncWorkerOptions.SectionName).Get<ChainSyncWorkerOptions>() ?? new ChainSyncWorkerOptions());
builder.Services.AddSingleton<ChainSyncSignal>();
builder.Services.AddHostedService<ChainSyncWorker>();

var app = builder.Build();

// The schema must exist before the outbox delivery service and the sync worker touch the database.
await app.Services.MigrateDatabaseAsync();

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

app.MapAddressEndpoints();
app.MapTransactionEndpoints();

await app.RunAsync();
