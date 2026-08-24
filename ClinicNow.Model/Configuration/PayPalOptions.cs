namespace ClinicNow.Model.Configuration;

/// <summary>PayPal sandbox credentials, sourced from <c>.env</c>. Wired in Phase 8 (payments + refund).</summary>
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
