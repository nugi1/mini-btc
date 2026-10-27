using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MiniBtc.Application.Abstractions;
using MiniBtc.Infrastructure.BitcoinCore;
using MiniBtc.Infrastructure.Persistence;
using Patterns.DDD.Application;
using Patterns.DDD.MassTransit;

namespace MiniBtc.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers the Core RPC client, configured from the "BitcoinCore" section.</summary>
    public static IServiceCollection AddBitcoinCoreRpc(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<BitcoinCoreOptions>(configuration.GetSection(BitcoinCoreOptions.SectionName));
        services.AddHttpClient<IBitcoinCoreRpc, BitcoinCoreRpcClient>();
        return services;
    }

    /// <summary>
    /// Registers the database (connection string "MiniBtc"), the chain store, the address index and
    /// the unit of work.
    /// Once MassTransit is registered with AddDomainEventOutbox, its interceptor is attached and
    /// domain events go into the outbox on every save.
    /// </summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("MiniBtc")
            ?? throw new InvalidOperationException("Connection string 'MiniBtc' is missing.");

        services.AddDbContext<MiniBtcDbContext>((provider, options) =>
        {
            options.UseNpgsql(connectionString);
            if (provider.GetService<DomainEventOutboxInterceptor>() is { } outbox)
                options.AddInterceptors(outbox);
        });
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<MiniBtcDbContext>());
        services.AddScoped<IChainStore, ChainStore>();
        services.AddScoped<IAddressIndex, AddressIndex>();
        return services;
    }
}
