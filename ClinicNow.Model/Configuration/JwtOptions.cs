namespace ClinicNow.Model.Configuration;

/// <summary>
/// JWT signing configuration, sourced from <c>.env</c>. Wired into
/// <c>AddAuthentication().AddJwtBearer(...)</c> in Phase 1.
/// </summary>
public class JwtOptions : EnvOptionsBase
{
    public string Key { get; }
    public string Issuer { get; }
    public string Audience { get; }
    public int ExpiryMinutes { get; }

    public JwtOptions()
    {
        Key = Require("JWT_KEY");
        Issuer = GetOrDefault("JWT_ISSUER", "ClinicNow");
        Audience = GetOrDefault("JWT_AUDIENCE", "ClinicNow");
        ExpiryMinutes = GetOrDefault("JWT_EXPIRY_MINUTES", 60);
    }
}
