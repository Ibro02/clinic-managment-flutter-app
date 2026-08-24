namespace ClinicNow.Model.Configuration;

/// <summary>Outbound email (SMTP) configuration, sourced from <c>.env</c>. Used only by the Worker (Phase 5) - never by the API.</summary>
public class SmtpOptions : EnvOptionsBase
{
    public string Host { get; }
    public int Port { get; }
    public string User { get; }
    public string Password { get; }
    public bool EnableSsl { get; }

    public SmtpOptions()
    {
        Host = GetOrDefault("SMTP_HOST", "smtp.gmail.com");
        Port = GetOrDefault("SMTP_PORT", 465);
        User = GetOrDefault("SMTP_USER", string.Empty);
        Password = GetOrDefault("SMTP_PASS", string.Empty);
        EnableSsl = GetOrDefault("SMTP_ENABLE_SSL", true);
    }
}
