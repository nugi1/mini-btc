using MiniBtc.Application.Abstractions;
using MiniBtc.Application.ChainSync;

namespace MiniBtc.Api.Hosting;

public sealed class ChainSyncWorkerOptions
{
    public const string SectionName = "ChainSyncWorker";

    /// <summary>
    /// How often to look for new blocks when no announcement arrives. ZMQ announcements wake the
    /// worker at once; this poll is the safety net, because ZMQ can lose notifications.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// The only place ChainSync runs: at startup, whenever ChainSyncSignal is raised (a ZMQ announcement),
/// and at the latest every PollInterval. Each run gets its own scope (its own DbContext, its own
/// transactions). It owns the logging: the use case only reports what happened.
/// </summary>
public sealed class ChainSyncWorker(
    IServiceScopeFactory scopes, ChainSyncSignal signal, ChainSyncWorkerOptions options, ILogger<ChainSyncWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string? lastProblem = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            lastProblem = await RunOnceAsync(lastProblem, stoppingToken);
            await signal.WaitAsync(options.PollInterval, stoppingToken);
        }
    }

    private async Task<string?> RunOnceAsync(string? lastProblem, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<ChainSynchronizer>().SyncToTipAsync(stoppingToken);

            if (result.IsFailure)
            {
                var problem = result.Error.ToString();
                if (problem != lastProblem) // a rejected block would otherwise be logged every few seconds
                    logger.LogError("Chain sync stopped: {Code} — {Message}", result.Error.Code, result.Error.Message);
                return problem;
            }

            if (result.Value.ConnectedBlocks > 0)
                logger.LogInformation("Connected {Count} block(s); tip is now height {Height} ({Hash})",
                    result.Value.ConnectedBlocks, result.Value.Tip.Height, result.Value.Tip.Hash);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or BitcoinCoreRpcException)
        {
            var problem = ex.Message;
            if (problem != lastProblem)
                logger.LogWarning("Bitcoin Core not reachable, will retry: {Message}", ex.Message);
            return problem;
        }
    }
}
