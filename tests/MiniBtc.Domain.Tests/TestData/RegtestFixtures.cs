using System.Text.Json;

namespace MiniBtc.Domain.Tests.TestData;

/// <summary>Blocks captured from our own regtest node by scripts/capture-fixtures.sh.</summary>
internal static class RegtestFixtures
{
    public static readonly string[] Names = ["genesis", "coinbase-only", "segwit-spend", "mixed"];

    /// <summary>getblock &lt;hash&gt; 0 as bytes, and getblock &lt;hash&gt; 2 as Core's JSON.</summary>
    public static (byte[] Raw, JsonElement Core) Load(string name)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "TestData", "regtest");
        var raw = Convert.FromHexString(File.ReadAllText(Path.Combine(directory, $"{name}.hex")).Trim());
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, $"{name}.json")));
        return (raw, json.RootElement.Clone());
    }
}
