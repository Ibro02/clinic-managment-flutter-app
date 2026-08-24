namespace ClinicNow.Model.Configuration;

/// <summary>
/// Base for every strongly-typed configuration group (<see cref="DatabaseOptions"/>,
/// <see cref="JwtOptions"/>, <see cref="RabbitMqOptions"/>, <see cref="SmtpOptions"/>,
/// <see cref="PayPalOptions"/>, <see cref="CorsOptions"/>).
///
/// All configuration is centralized in the git-ignored <c>.env</c> file (loaded into
/// process environment variables by DotNetEnv at startup, or supplied directly as
/// container env vars in docker-compose) - never in <c>appsettings.json</c>, never
/// hardcoded (rulebook Part II §C).
///
/// Each concrete Options type reads its variables exactly once, in its constructor,
/// and is registered as a DI singleton in <c>Program.cs</c>. Every other class
/// receives the already-parsed instance through constructor injection instead of
/// calling <see cref="Environment.GetEnvironmentVariable(string)"/> itself
/// (rulebook Part II §D: "Environment varijable treba citati jednom u konstruktoru,
/// a ne pri svakom pozivu").
/// </summary>
public abstract class EnvOptionsBase
{
    /// <summary>Reads a required value; throws a clear startup error if it's missing.</summary>
    protected static string Require(string key)
    {
        var value = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Missing required configuration value '{key}'. Set it in your .env file (see .env.example).");
        }

        return value;
    }

    protected static string GetOrDefault(string key, string defaultValue) =>
        Environment.GetEnvironmentVariable(key) is { Length: > 0 } value ? value : defaultValue;

    protected static int GetOrDefault(string key, int defaultValue) =>
        int.TryParse(Environment.GetEnvironmentVariable(key), out var value) ? value : defaultValue;

    protected static bool GetOrDefault(string key, bool defaultValue) =>
        bool.TryParse(Environment.GetEnvironmentVariable(key), out var value) ? value : defaultValue;
}
