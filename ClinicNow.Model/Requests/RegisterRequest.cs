namespace ClinicNow.Model.Requests;

/// <summary>
/// Self-registration payload for the mobile app. There is deliberately no
/// <c>Role</c>/<c>IsAdmin</c> field - self-registration always creates a
/// <c>Patient</c>-role account; the server never trusts a client-supplied role
/// (rulebook §5: "Register endpoint must not accept role/isAdmin from the client").
/// Staff/Doctor/Administrator accounts are created by an administrator instead
/// (desktop app, Phase 3+), never through this endpoint.
/// </summary>
public class RegisterRequest
{
    public string Email { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }
}
