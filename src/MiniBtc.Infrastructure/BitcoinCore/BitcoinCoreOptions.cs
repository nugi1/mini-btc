namespace MiniBtc.Infrastructure.BitcoinCore;

/// <summary>
/// Where Core's RPC listens and how to log in. The defaults match docker-compose.yml and
/// .env.example (regtest RPC on 127.0.0.1:18443).
/// </summary>
public sealed class BitcoinCoreOptions
{
    public const string SectionName = "BitcoinCore";

    public Uri Url { get; set; } = new("http://127.0.0.1:18443/");
    public string User { get; set; } = "minibtc";
    public string Password { get; set; } = "minibtc-regtest";
}
