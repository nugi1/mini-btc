using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MiniBtc.Application.Contracts;
using MiniBtc.Infrastructure.Messaging;
using MiniBtc.Infrastructure.Persistence;
using MiniBtc.Infrastructure.Zmq;
using MiniBtc.Integration.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Patterns.DDD.MassTransit;

namespace MiniBtc.Integration.Tests.Zmq;

/// <summary>
/// End to end against the compose services: Core mines a block, pushes it over ZMQ rawblock, our
/// subscriber publishes BlockAnnounced to RabbitMQ with the hash-derived message id, and a consumer
/// receives it through the inbox. Note: this test mines one real block on your regtest chain.
/// </summary>
public sealed class ZmqAnnouncementTests : IAsyncLifetime
{
    // HASH160 of private key 1's compressed public key, as a regtest P2PKH address.
    private const string MiningAddress = "mrCDrCybB6J1vRfbwM5hemdJz73FwDBC8r";
    private readonly TestDatabase _database = new();

    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task A_block_mined_by_core_is_announced_on_the_bus_with_its_hash_as_identity()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<Announcements>();
        builder.Services.AddDbContext<MiniBtcDbContext>((provider, options) => options
            .UseNpgsql(_database.ConnectionString)
            .UseDomainEventOutbox(provider));
        builder.Services.AddMessaging(
            builder.Configuration,
            x => x.AddConsumer<RecordingConsumer>().Endpoint(e => e.Temporary = true),
            endpointPrefix: $"test-{Guid.NewGuid():N}");
        builder.Services.AddZmqBlockNotifications(builder.Configuration);

        using var host = builder.Build();
        await host.StartAsync();
        await Task.Delay(TimeSpan.FromSeconds(1)); // ZMQ subscriptions take a moment to reach the publisher

        var minedHash = await MineOneBlockAsync();

        var announcements = host.Services.GetRequiredService<Announcements>();
        var received = await announcements.WaitForAsync(minedHash, TimeSpan.FromSeconds(20));
        Assert.NotNull(received);
        Assert.Equal(MessageIds.FromKey($"block:{minedHash}"), received.Value.MessageId);
        await host.StopAsync();
    }

    /// <summary>generatetoaddress over Core's JSON-RPC; returns the new block's hash.</summary>
    private static async Task<string> MineOneBlockAsync()
    {
        using var http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:18443/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("minibtc:minibtc-regtest")));
        using var response = await http.PostAsJsonAsync("",
            new { jsonrpc = "2.0", id = 1, method = "generatetoaddress", @params = new object[] { 1, MiningAddress } });
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("result")[0].GetString()!;
    }

    public sealed class Announcements
    {
        private readonly ConcurrentQueue<(BlockAnnounced Message, Guid? MessageId)> _received = new();

        public void Add(BlockAnnounced message, Guid? messageId) => _received.Enqueue((message, messageId));

        public async Task<(BlockAnnounced Message, Guid? MessageId)?> WaitForAsync(string blockHash, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                foreach (var entry in _received)
                {
                    if (entry.Message.BlockHash == blockHash) return entry;
                }
                await Task.Delay(50);
            }
            return null;
        }
    }

    public sealed class RecordingConsumer(Announcements announcements) : IConsumer<BlockAnnounced>
    {
        public Task Consume(ConsumeContext<BlockAnnounced> context)
        {
            announcements.Add(context.Message, context.MessageId);
            return Task.CompletedTask;
        }
    }
}
