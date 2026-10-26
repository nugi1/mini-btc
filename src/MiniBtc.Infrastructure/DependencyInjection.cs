using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MiniBtc.Application.Abstractions;
using MiniBtc.Infrastructure.BitcoinCore;

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
}
