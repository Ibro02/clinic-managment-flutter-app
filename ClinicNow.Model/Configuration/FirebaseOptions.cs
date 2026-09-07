namespace ClinicNow.Model.Configuration;

/// <summary>
/// Firebase Cloud Messaging configuration, sourced from <c>.env</c> like every
/// other option group (rulebook Part II §C). Used by the Worker to send pushes;
/// the API never talks to FCM.
///
/// The credential is the service-account JSON, carried base64-encoded in a
/// single variable because a multi-line PEM private key cannot survive a
/// <c>.env</c> file intact. It is decoded once, here, in the constructor.
///
/// Both values are optional on purpose: with no credential configured
/// <see cref="IsConfigured"/> is false and the Worker logs and skips instead of
/// crashing. Push is an enhancement on top of a notification that is already
/// persisted and already delivered in-app - a developer without Firebase
/// credentials must still be able to run the whole stack.
/// </summary>
public class FirebaseOptions : EnvOptionsBase
{
    /// <summary>Firebase project id, e.g. <c>clinicnow-66a86</c> - part of the FCM v1 endpoint path.</summary>
    public string ProjectId { get; }

    /// <summary>The decoded service-account JSON, or empty when not configured.</summary>
    public string ServiceAccountJson { get; }

    /// <summary>Whether a project id and a credential are both present.</summary>
    public bool IsConfigured => ProjectId.Length > 0 && ServiceAccountJson.Length > 0;

    public FirebaseOptions()
    {
        ProjectId = GetOrDefault("FIREBASE_PROJECT_ID", string.Empty);

        var encoded = GetOrDefault("FIREBASE_SERVICE_ACCOUNT_BASE64", string.Empty);
        ServiceAccountJson = encoded.Length == 0 ? string.Empty : Decode(encoded);
    }

    /// <summary>
    /// A malformed credential is a configuration mistake, not a runtime
    /// condition to tolerate: failing at startup with a message naming the
    /// variable beats every push silently failing later with an auth error.
    /// </summary>
    private static string Decode(string encoded)
    {
        try
        {
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                "FIREBASE_SERVICE_ACCOUNT_BASE64 is not valid base64. Re-encode the service-account JSON file, e.g. " +
                "base64 -w0 clinicnow-firebase-adminsdk.json", ex);
        }
    }
}
