using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MiniBtc.Infrastructure.Persistence;

/// <summary>
/// Lets `dotnet ef` create the context without starting the API (and without MassTransit).
/// Migrations only need the model; the connection string matters only for `dotnet ef database update`.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<MiniBtcDbContext>
{
    public MiniBtcDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<MiniBtcDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("MINIBTC_CONNECTION")
                       ?? "Host=127.0.0.1;Port=5433;Database=minibtc;Username=minibtc;Password=minibtc")
            .Options);
}
