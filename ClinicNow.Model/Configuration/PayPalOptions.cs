namespace ClinicNow.Model.Configuration;

/// <summary>
/// PayPal sandbox credentials, sourced from `.env` (rulebook Part II §C).
/// Mirrors `SmtpOptions`'s pattern: `GetOrDefault` with an empty-string
/// default rather than `Require`, so the API still boots cleanly in phases/
/// environments where PayPal isn't configured yet - `PayPalClient` itself is
/// where a genuinely missing credential surfaces, at the point it's actually
/// needed, not at startup.
/// </summary>
public class PayPalOptions : EnvOptionsBase
{
    public string ClientId { get; }
    public string ClientSecret { get; }
    public string Mode { get; }

    public PayPalOptions()
    {
        ClientId = GetOrDefault("PAYPAL_CLIENT_ID", string.Empty);
        ClientSecret = GetOrDefault("PAYPAL_CLIENT_SECRET", string.Empty);
        Mode = GetOrDefault("PAYPAL_MODE", "sandbox");
    }
}
