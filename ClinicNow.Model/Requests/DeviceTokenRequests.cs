namespace ClinicNow.Model.Requests;

/// <summary>
/// Registers the calling device for push notifications. Carries no user id: the
/// owner is whoever the JWT says, so a client cannot subscribe someone else's
/// account to its own device (rulebook Part II §F).
/// </summary>
public class RegisterDeviceTokenRequest
{
    public string Token { get; set; } = string.Empty;

    /// <summary>"Android" or "Windows" - diagnostics only, never a permission.</summary>
    public string Platform { get; set; } = string.Empty;
}

/// <summary>
/// Unregisters a device, on sign-out. The token is the identifier rather than a
/// row id, because that is what the client holds.
/// </summary>
public class UnregisterDeviceTokenRequest
{
    public string Token { get; set; } = string.Empty;
}
