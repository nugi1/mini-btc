using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MiniBtc.Application.Abstractions;
using MiniBtc.Domain.Blocks;
using MiniBtc.Domain.Hashing;
using MiniBtc.Infrastructure.BitcoinCore;

namespace MiniBtc.Infrastructure.Tests.BitcoinCore;

/// <summary>
/// The client against canned replies in the exact shape Core v30 sends for JSON-RPC 2.0 requests
/// (rpc/request.cpp JSONRPCReplyObj): HTTP 200 with "result" or "error", no Core process needed.
/// The live round trip is covered by the integration tests against docker compose.
/// </summary>
public class BitcoinCoreRpcClientTests
{
    private const string GenesisHash = "0f9188f13cb7b2c71f2a335e3a4fc328bf5beb436012afca590b1a11466e2206";

    // getblock <genesis> 0 as captured from our regtest node (scripts/capture-fixtures.sh).
    private const string GenesisBlockHex =
        "0100000000000000000000000000000000000000000000000000000000000000000000003ba3edfd7a7b12b27ac72c3e" +
        "67768f617fc81bc3888a51323a9fb8aa4b1e5e4adae5494dffff7f200200000001010000000100000000000000000000" +
        "00000000000000000000000000000000000000000000ffffffff4d04ffff001d0104455468652054696d65732030332f" +
        "4a616e2f32303039204368616e63656c6c6f72206f6e206272696e6b206f66207365636f6e64206261696c6f75742066" +
        "6f722062616e6b73ffffffff0100f2052a01000000434104678afdb0fe5548271967f1a67130b7105cd6a828e03909a6" +
        "7962e0ea1f61deb649f6bc3f4cef38c4f35504e51ec112de5c384df7ba0b8d578a4c702b6bf11d5fac00000000";

    [Fact]
    public async Task Sends_a_json_rpc_2_0_post_with_basic_auth()
    {
        var handler = new StubHandler(HttpStatusCode.OK, $$"""{"jsonrpc":"2.0","result":"{{GenesisHash}}","id":1}""");
        var client = CreateClient(handler);

        await client.GetBlockHashAsync(0);

        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri("http://127.0.0.1:18443/"), request.Uri);
        Assert.Equal("Basic", request.Authorization!.Scheme);
        Assert.Equal("minibtc:minibtc-regtest", Encoding.UTF8.GetString(Convert.FromBase64String(request.Authorization.Parameter!)));

        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal("2.0", body.RootElement.GetProperty("jsonrpc").GetString());
        Assert.Equal(1, body.RootElement.GetProperty("id").GetInt64());
        Assert.Equal("getblockhash", body.RootElement.GetProperty("method").GetString());
        Assert.Equal("[0]", body.RootElement.GetProperty("params").GetRawText());
    }

    [Fact]
    public async Task Getblockhash_returns_the_hash_in_display_order()
    {
        var handler = new StubHandler(HttpStatusCode.OK, $$"""{"jsonrpc":"2.0","result":"{{GenesisHash}}","id":1}""");

        var hash = await CreateClient(handler).GetBlockHashAsync(0);

        Assert.Equal(UInt256.Parse(GenesisHash), hash);
    }

    [Fact]
    public async Task Getblock_asks_for_verbosity_0_and_returns_bytes_our_decoder_accepts()
    {
        var handler = new StubHandler(HttpStatusCode.OK, $$"""{"jsonrpc":"2.0","result":"{{GenesisBlockHex}}","id":1}""");

        var raw = await CreateClient(handler).GetRawBlockAsync(UInt256.Parse(GenesisHash));

        using var body = JsonDocument.Parse(handler.Requests.Single().Body);
        Assert.Equal("getblock", body.RootElement.GetProperty("method").GetString());
        Assert.Equal($$"""["{{GenesisHash}}",0]""", body.RootElement.GetProperty("params").GetRawText());
        Assert.Equal(285, raw.Length);
        Assert.Equal(GenesisHash, Block.Parse(raw).Hash.ToString());
    }

    [Fact]
    public async Task Getblockchaininfo_maps_chain_height_and_tip()
    {
        const string tip = "3680d858d8cd4cff51cfa01c900dfc15f70218ee2de9f92e287c71763faa06c5";
        // Three $: the JSON itself ends in "}}", which a $$ string would read as closing a hole.
        var handler = new StubHandler(HttpStatusCode.OK, $$$"""
            {"jsonrpc":"2.0","id":1,"result":{"chain":"regtest","blocks":104,"headers":104,
             "bestblockhash":"{{{tip}}}","difficulty":4.656542373906925e-10,"initialblockdownload":false,"pruned":false}}
            """);

        var info = await CreateClient(handler).GetBlockchainInfoAsync();

        Assert.Equal(new BlockchainInfo("regtest", 104, 104, UInt256.Parse(tip)), info);
    }

    [Theory]
    [InlineData(RpcErrorCodes.InvalidParameter, "Block height out of range")]
    [InlineData(RpcErrorCodes.InvalidAddressOrKey, "Block not found")]
    [InlineData(RpcErrorCodes.InWarmup, "Loading block index…")]
    public async Task Core_errors_arrive_as_http_200_and_surface_with_their_code(int code, string message)
    {
        var handler = new StubHandler(HttpStatusCode.OK,
            $$"""{"jsonrpc":"2.0","error":{"code":{{code}},"message":"{{message}}"},"id":1}""");

        var ex = await Assert.ThrowsAsync<BitcoinCoreRpcException>(() => CreateClient(handler).GetBlockHashAsync(999));

        Assert.Equal("getblockhash", ex.Method);
        Assert.Equal(code, ex.Code);
        Assert.Equal(message, ex.CoreMessage);
    }

    [Fact]
    public async Task Wrong_credentials_are_an_http_401_with_no_code()
    {
        var handler = new StubHandler(HttpStatusCode.Unauthorized, "");

        var ex = await Assert.ThrowsAsync<BitcoinCoreRpcException>(() => CreateClient(handler).GetBlockchainInfoAsync());

        Assert.Null(ex.Code);
        Assert.Contains("401", ex.Message);
    }

    [Fact]
    public async Task Negative_height_is_rejected_before_calling_core()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => CreateClient(handler).GetBlockHashAsync(-1));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Each_call_gets_a_new_id()
    {
        var handler = new StubHandler(HttpStatusCode.OK, $$"""{"jsonrpc":"2.0","result":"{{GenesisHash}}","id":1}""");
        var client = CreateClient(handler);

        await client.GetBlockHashAsync(0);
        await client.GetBlockHashAsync(0);

        var ids = handler.Requests.Select(r => JsonDocument.Parse(r.Body).RootElement.GetProperty("id").GetInt64()).ToArray();
        Assert.Equal(new long[] { 1, 2 }, ids);
    }

    private static BitcoinCoreRpcClient CreateClient(StubHandler handler) =>
        new(new HttpClient(handler), Options.Create(new BitcoinCoreOptions()));

    private sealed record RecordedRequest(HttpMethod Method, Uri? Uri, System.Net.Http.Headers.AuthenticationHeaderValue? Authorization, string Body);

    private sealed class StubHandler(HttpStatusCode status, string responseBody) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri, request.Headers.Authorization, body));
            return new HttpResponseMessage(status) { Content = new StringContent(responseBody, Encoding.UTF8, "application/json") };
        }
    }
}
