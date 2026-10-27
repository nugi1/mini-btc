using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MiniBtc.Application.Abstractions;
using MiniBtc.Domain.Hashing;

namespace MiniBtc.Infrastructure.BitcoinCore;

/// <summary>
/// JSON-RPC 2.0 over HTTP to Bitcoin Core: one POST per call, HTTP Basic auth with the rpcuser /
/// rpcpassword from docker-compose. With the "2.0" marker Core answers every call it understood
/// with HTTP 200, carrying either "result" or an "error" object {code, message} (httprpc.cpp);
/// only a transport problem, such as wrong credentials (401), shows up as an HTTP status.
/// </summary>
public sealed class BitcoinCoreRpcClient : IBitcoinCoreRpc
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly HttpClient _http;
    private long _nextId;

    public BitcoinCoreRpcClient(HttpClient http, IOptions<BitcoinCoreOptions> options)
    {
        var settings = options.Value;
        http.BaseAddress = settings.Url;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.User}:{settings.Password}")));
        _http = http;
    }

    public async Task<BlockchainInfo> GetBlockchainInfoAsync(CancellationToken cancellationToken = default)
    {
        var result = await CallAsync("getblockchaininfo", [], cancellationToken);
        return new BlockchainInfo(
            result.GetProperty("chain").GetString()!,
            result.GetProperty("blocks").GetInt32(),
            result.GetProperty("headers").GetInt32(),
            UInt256.Parse(result.GetProperty("bestblockhash").GetString()!));
    }

    public async Task<UInt256> GetBlockHashAsync(int height, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        var result = await CallAsync("getblockhash", [height], cancellationToken);
        return UInt256.Parse(result.GetString()!);
    }

    public async Task<byte[]> GetRawBlockAsync(UInt256 blockHash, CancellationToken cancellationToken = default)
    {
        // Verbosity 0: the serialized block as hex. Core's decoded forms (1, 2, 3) are for people.
        var result = await CallAsync("getblock", [blockHash.ToString(), 0], cancellationToken);
        return Convert.FromHexString(result.GetString()!);
    }

    public async Task<UInt256> SendRawTransactionAsync(string hex, CancellationToken cancellationToken = default)
    {
        var result = await CallAsync("sendrawtransaction", [hex], cancellationToken);
        return UInt256.Parse(result.GetString()!);
    }

    public async Task<byte[]> GetRawTransactionAsync(UInt256 txid, CancellationToken cancellationToken = default)
    {
        var result = await CallAsync("getrawtransaction", [txid.ToString()], cancellationToken);
        return Convert.FromHexString(result.GetString()!);
    }

    public async Task<RawMempool> GetRawMempoolAsync(CancellationToken cancellationToken = default)
    {
        // verbose=false, mempool_sequence=true: {"txids": [...], "mempool_sequence": n}
        var result = await CallAsync("getrawmempool", [false, true], cancellationToken);
        var txids = result.GetProperty("txids").EnumerateArray().Select(t => UInt256.Parse(t.GetString()!)).ToList();
        return new RawMempool(txids, result.GetProperty("mempool_sequence").GetUInt64());
    }

    public async Task<TxOutSetInfo> GetTxOutSetInfoAsync(CancellationToken cancellationToken = default)
    {
        var result = await CallAsync("gettxoutsetinfo", [], cancellationToken);
        return new TxOutSetInfo(
            result.GetProperty("height").GetInt32(),
            UInt256.Parse(result.GetProperty("bestblock").GetString()!),
            result.GetProperty("txouts").GetInt64(),
            ToSatoshis(result.GetProperty("total_amount")));
    }

    public async Task<TxOutSetScan> ScanTxOutSetAsync(string descriptor, CancellationToken cancellationToken = default)
    {
        var result = await CallAsync("scantxoutset", ["start", new[] { descriptor }], cancellationToken);
        return new TxOutSetScan(
            result.GetProperty("height").GetInt32(),
            UInt256.Parse(result.GetProperty("bestblock").GetString()!),
            result.GetProperty("unspents").GetArrayLength(),
            ToSatoshis(result.GetProperty("total_amount")));
    }

    /// <summary>Core prints amounts as BTC with up to 8 decimals; read as decimal, never double, then scale exactly.</summary>
    private static long ToSatoshis(JsonElement btc) => decimal.ToInt64(btc.GetDecimal() * 100_000_000m);

    private async Task<JsonElement> CallAsync(string method, object[] parameters, CancellationToken cancellationToken)
    {
        var request = new RpcRequest("2.0", Interlocked.Increment(ref _nextId), method, parameters);
        using var response = await _http.PostAsJsonAsync("", request, JsonOptions, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new BitcoinCoreRpcException(method, null,
                "Core rejected the RPC credentials (HTTP 401). Check BitcoinCore:User and BitcoinCore:Password " +
                "against BTC_RPC_USER and BTC_RPC_PASSWORD in .env.");
        if (!response.IsSuccessStatusCode)
            throw new BitcoinCoreRpcException(method, null, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
        var root = json.RootElement;

        if (root.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
            throw new BitcoinCoreRpcException(method, error.GetProperty("code").GetInt32(), error.GetProperty("message").GetString() ?? "");

        return root.GetProperty("result").Clone();
    }

    private sealed record RpcRequest(string Jsonrpc, long Id, string Method, object[] Params);
}
