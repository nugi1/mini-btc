using Microsoft.EntityFrameworkCore;
using MiniBtc.Infrastructure.Persistence;

namespace MiniBtc.Integration.Tests.Persistence;

/// <summary>
/// A throwaway database on the compose PostgreSQL (127.0.0.1:5433), migrated to the current schema
/// and dropped afterwards. Override the server with MINIBTC_TEST_POSTGRES.
/// </summary>
public sealed class TestDatabase : IAsyncLifetime
{
    private readonly string _connectionString;

    public TestDatabase()
    {
        var server = Environment.GetEnvironmentVariable("MINIBTC_TEST_POSTGRES")
                     ?? "Host=127.0.0.1;Port=5433;Username=minibtc;Password=minibtc";
        _connectionString = $"{server};Database=minibtc_test_{Guid.NewGuid():N}";
    }

    /// <summary>A new context, as a fresh scope (a new request, a restart) would get.</summary>
    public MiniBtcDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<MiniBtcDbContext>().UseNpgsql(_connectionString).Options);

    public async Task InitializeAsync()
    {
        await using var db = CreateContext();
        try
        {
            await db.Database.MigrateAsync();
        }
        catch (Exception ex) when (ex is Npgsql.NpgsqlException or System.Net.Sockets.SocketException)
        {
            throw new InvalidOperationException(
                "PostgreSQL is not reachable on 127.0.0.1:5433. Start it with `docker compose up -d --wait`.", ex);
        }
    }

    public async Task DisposeAsync()
    {
        await using var db = CreateContext();
        await db.Database.EnsureDeletedAsync();
    }
}
