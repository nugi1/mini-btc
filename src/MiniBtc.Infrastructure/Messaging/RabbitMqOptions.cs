namespace MiniBtc.Infrastructure.Messaging;

/// <summary>RabbitMQ connection; the defaults match docker-compose.yml and .env.example.</summary>
public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string Host { get; set; } = "127.0.0.1";
    public ushort Port { get; set; } = 5672;
    public string VirtualHost { get; set; } = "/";
    public string Username { get; set; } = "minibtc";
    public string Password { get; set; } = "minibtc";
}
