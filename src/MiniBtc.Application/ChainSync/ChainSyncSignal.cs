namespace MiniBtc.Application.ChainSync;

/// <summary>
/// Wakes the one component that runs ChainSync. Requests coalesce: however many arrive while a sync
/// runs, they cause exactly one follow-up run, which covers all of them because a sync always goes
/// to Core's current tip. Keeping a single runner means two syncs never write at the same time.
/// </summary>
public sealed class ChainSyncSignal
{
    private readonly SemaphoreSlim _pending = new(initialCount: 0, maxCount: 1);

    public void Request()
    {
        try
        {
            _pending.Release();
        }
        catch (SemaphoreFullException)
        {
            // A run is already pending; it will cover this request too.
        }
    }

    /// <summary>True if a request arrived, false if <paramref name="timeout"/> passed first.</summary>
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _pending.WaitAsync(timeout, cancellationToken);
}
