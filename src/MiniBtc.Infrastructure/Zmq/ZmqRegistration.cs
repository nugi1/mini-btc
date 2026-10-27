using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MiniBtc.Infrastructure.Zmq;

public static class ZmqRegistration
{
    /// <summary>Subscribes to Core's ZMQ rawblock and turns each notification into a BlockAnnounced message.</summary>
    public static IServiceCollection AddZmqBlockNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(configuration.GetSection(ZmqOptions.SectionName).Get<ZmqOptions>() ?? new ZmqOptions());
        services.AddHostedService<ZmqBlockSubscriber>();
        return services;
    }

    /// <summary>Subscribes to Core's ZMQ sequence topic: reorg-aware block events and mempool adds/removes.</summary>
    public static IServiceCollection AddZmqSequenceNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(configuration.GetSection(ZmqOptions.SectionName).Get<ZmqOptions>() ?? new ZmqOptions());
        services.AddHostedService<ZmqSequenceSubscriber>();
        return services;
    }
}
