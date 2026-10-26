using MiniBtc.Application.ChainSync;

namespace MiniBtc.Application.Tests.ChainSync;

public class ChainSyncSignalTests
{
    [Fact]
    public async Task A_request_wakes_the_waiter()
    {
        var signal = new ChainSyncSignal();
        signal.Request();

        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None));
    }

    [Fact]
    public async Task Without_a_request_the_wait_times_out()
    {
        Assert.False(await new ChainSyncSignal().WaitAsync(TimeSpan.FromMilliseconds(20), CancellationToken.None));
    }

    [Fact]
    public async Task Requests_made_during_a_run_collapse_into_one_follow_up()
    {
        var signal = new ChainSyncSignal();
        signal.Request();
        signal.Request();
        signal.Request();

        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None));
        Assert.False(await signal.WaitAsync(TimeSpan.FromMilliseconds(20), CancellationToken.None));
    }
}
