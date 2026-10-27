using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MiniBtc.Infrastructure.BitcoinCore;

namespace MiniBtc.Integration.Tests.BitcoinCore;

/// <summary>
/// Any Core RPC by name, for driving the compose bitcoind from tests (mining, invalidateblock,
/// gettxoutsetinfo): things the node itself never asks Core to do.
/// </summary>
public sealed class CoreTestRpc : IDisposable
{
    /// <summary>P2PKH of private key 1. Tests mine to it; nobody else spends from it.</summary>
    public const string KeyOneAddress = "mrCDrCybB6J1vRfbwM5hemdJz73FwDBC8r";

    /// <summary>P2PKH of private key 2. Mining a competing branch to another address keeps its blocks distinct.</summary>
    public const string KeyTwoAddress = "mg8Jz5776UdyiYcBb9Z873NTozEiADRW5H";

    private readonly HttpClient _http = new() { BaseAddress = new Uri("http://127.0.0.1:18443/") };

    public CoreTestRpc()
    {
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("minibtc:minibtc-regtest")));
    }

    /// <summary>The node's own client, pointed at the same Core.</summary>
    public static BitcoinCoreRpcClient NodeClient() => new(new HttpClient(), Options.Create(new BitcoinCoreOptions()));

    public async Task<JsonElement> CallAsync(string method, params object[] parameters)
    {
        using var response = await _http.PostAsJsonAsync("", new { jsonrpc = "2.0", id = 1, method, @params = parameters });
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (json.RootElement.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
            throw new InvalidOperationException($"{method}: {error.GetProperty("message").GetString()}");
        return json.RootElement.GetProperty("result").Clone();
    }

    public async Task<IReadOnlyList<string>> MineAsync(int blocks, string address = KeyOneAddress) =>
        (await CallAsync("generatetoaddress", blocks, address)).EnumerateArray().Select(h => h.GetString()!).ToList();

    public void Dispose() => _http.Dispose();
}
