using System.Collections.Concurrent;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MiniBtc.Application.Contracts;
using MiniBtc.Domain.Consensus;
using MiniBtc.Infrastructure.Messaging;
using MiniBtc.Infrastructure.Persistence;
using MiniBtc.Integration.Tests.Persistence;
using Patterns.DDD.MassTransit;
using static MiniBtc.Domain.Tests.TestData.TestChain;

namespace MiniBtc.Integration.Tests.Messaging;

/// <summary>
/// The event path end to end on the compose services: UtxoSet raises BlockConnected → the save writes
/// an outbox row in the block's own transaction → MassTransit publishes BlockConnectedV1 to RabbitMQ
/// → a consumer receives it through the inbox.
/// </summary>
public sealed class OutboxTests : IAsyncLifetime
{
    private readonly TestDatabase _database = new();

    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task A_committed_block_reaches_a_rabbitmq_consumer_as_BlockConnectedV1()
    {
        using var host = await StartHostAsync();
        var received = host.Services.GetRequiredService<Received>();
        var block = MinedBlock(Genesis, Coinbase(1));

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MiniBtcDbContext>();
            var store = new ChainStore(db);
            var utxos = await store.LoadAsync(NetworkParams.Regtest);
            await db.SaveChangesAsync();

            store.AddConnectedBlock(block, utxos.Connect(block).Value);
            await db.SaveChangesAsync();
        }

        var message = await received.WaitForAsync(m => m.BlockHash == block.Hash.ToString(), TimeSpan.FromSeconds(20));
        Assert.Equal(new BlockConnectedV1(block.Hash.ToString(), 1, 1), message);
        await host.StopAsync();
    }

    [Fact]
    public async Task A_save_that_rolls_back_publishes_nothing()
    {
        using var host = await StartHostAsync();
        var received = host.Services.GetRequiredService<Received>();
        var block = MinedBlock(Genesis, Coinbase(1));

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MiniBtcDbContext>();
            await new ChainStore(db).LoadAsync(NetworkParams.Regtest);
            await db.SaveChangesAsync(); // genesis is now in the database

            // Start over as a later run would: genesis exists only in the database, not in this context.
            db.ChangeTracker.Clear();
            var store = new ChainStore(db);
            var utxos = await store.LoadAsync(NetworkParams.Regtest);

            store.AddConnectedBlock(block, utxos.Connect(block).Value);
            // A second row with the genesis block's hash: PostgreSQL's primary key rejects it, so the
            // whole transaction rolls back, the outbox row for BlockConnected included.
            db.Blocks.Add(new BlockRecord { Hash = Genesis.ToWireBytes(), Height = 999 });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        var message = await received.WaitForAsync(m => m.BlockHash == block.Hash.ToString(), TimeSpan.FromSeconds(3));
        Assert.Null(message);
        await host.StopAsync();
    }

    private async Task<IHost> StartHostAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<Received>();
        builder.Services.AddDbContext<MiniBtcDbContext>((provider, options) => options
            .UseNpgsql(_database.ConnectionString)
            .UseDomainEventOutbox(provider));
        builder.Services.AddMessaging(
            builder.Configuration,
            x => x.AddConsumer<RecordingConsumer>().Endpoint(e => e.Temporary = true), // auto-deleted queue
            endpointPrefix: $"test-{Guid.NewGuid():N}",
            configureOutbox: outbox => outbox.QueryDelay = TimeSpan.FromMilliseconds(100));

        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    public sealed class Received
    {
        private readonly ConcurrentQueue<BlockConnectedV1> _messages = new();

        public void Add(BlockConnectedV1 message) => _messages.Enqueue(message);

        public async Task<BlockConnectedV1?> WaitForAsync(Func<BlockConnectedV1, bool> match, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (_messages.FirstOrDefault(match) is { } found) return found;
                await Task.Delay(50);
            }
            return null;
        }
    }

    public sealed class RecordingConsumer(Received received) : IConsumer<BlockConnectedV1>
    {
        public Task Consume(ConsumeContext<BlockConnectedV1> context)
        {
            received.Add(context.Message);
            return Task.CompletedTask;
        }
    }
}
