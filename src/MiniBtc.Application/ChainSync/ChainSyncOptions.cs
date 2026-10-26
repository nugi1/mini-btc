namespace MiniBtc.Application.ChainSync;

public sealed class ChainSyncOptions
{
    public const string SectionName = "ChainSync";

    /// <summary>The chain Core must report in getblockchaininfo.</summary>
    public string ExpectedChain { get; set; } = "regtest";

    /// <summary>Wait between attempts while Core answers -28 (still loading its block index).</summary>
    public TimeSpan WarmupRetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Give up waiting for Core's warmup after this many attempts.</summary>
    public int WarmupMaxAttempts { get; set; } = 60;
}
