using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MiniBtc.Infrastructure.Persistence;
using Patterns.DDD.MassTransit;

namespace MiniBtc.Infrastructure.Messaging;

public static class MessagingRegistration
{
    /// <summary>
    /// MassTransit on RabbitMQ with Patterns.DDD's EF Core outbox and inbox. Domain events raised by
    /// aggregates are mapped (IntegrationEvents) and written as outbox rows in the same database
    /// transaction as the change that raised them; MassTransit's delivery service then publishes them.
    /// Every consumer endpoint gets the inbox, so a redelivered message is processed once.
    /// </summary>
    public static IServiceCollection AddMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator>? configureConsumers = null,
        string? endpointPrefix = null,
        Action<IEntityFrameworkOutboxConfigurator>? configureOutbox = null)
    {
        var rabbit = configuration.GetSection(RabbitMqOptions.SectionName).Get<RabbitMqOptions>() ?? new RabbitMqOptions();

        services.AddMassTransit(x =>
        {
            x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter(endpointPrefix ?? "minibtc", includeNamespace: false));
            x.AddDomainEventOutbox<MiniBtcDbContext>(IntegrationEvents.Configure, configureOutbox);
            configureConsumers?.Invoke(x);

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(rabbit.Host, rabbit.Port, rabbit.VirtualHost, host =>
                {
                    host.Username(rabbit.Username);
                    host.Password(rabbit.Password);
                });
                cfg.ConfigureEndpoints(context);
            });
        });
        return services;
    }

    /// <summary>Applies pending EF Core migrations; run once at startup, before anything reads the database.</summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MiniBtcDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
