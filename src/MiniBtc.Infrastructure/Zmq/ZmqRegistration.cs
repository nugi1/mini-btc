using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MiniBtc.Infrastructure.Zmq;

public static class ZmqRegistration
{
    /// <summary>Subscribes to Core's ZMQ rawblock and turns each notification into a BlockAnnounced message.</summary>
    public static IServiceCollection AddZmqBlockNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(configuration.GetSection(ZmqOptions.SectionName).Get<ZmqOptions>() ?? new ZmqOptions());
        services.AddHostedService<ZmqBlockSubscriber>();
        return services;
    }
}
