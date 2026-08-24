namespace ClinicNow.Model.Configuration;

/// <summary>RabbitMQ broker connection, sourced from <c>.env</c>. Shared by the API (publisher) and the Worker (consumer).</summary>
public class RabbitMqOptions : EnvOptionsBase
{
    public string Host { get; }
    public int Port { get; }
    public string User { get; }
    public string Password { get; }

    public RabbitMqOptions()
    {
        Host = GetOrDefault("RABBITMQ_HOST", "localhost");
        Port = GetOrDefault("RABBITMQ_PORT", 5672);
        User = GetOrDefault("RABBITMQ_USER", "guest");
        Password = GetOrDefault("RABBITMQ_PASS", "guest");
    }
}
