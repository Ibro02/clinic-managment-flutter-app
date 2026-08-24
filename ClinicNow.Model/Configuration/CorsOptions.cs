namespace ClinicNow.Model.Configuration;

/// <summary>
/// Explicit CORS allow-list, sourced from <c>.env</c> (comma-separated
/// <c>CORS_ALLOWED_ORIGINS</c>). Never <c>*</c> - see CLAUDE.md and rulebook Part II §D.
/// </summary>
public class CorsOptions : EnvOptionsBase
{
    public string[] AllowedOrigins { get; }

    public CorsOptions()
    {
        AllowedOrigins = GetOrDefault("CORS_ALLOWED_ORIGINS", string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
