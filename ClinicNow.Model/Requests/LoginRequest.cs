namespace ClinicNow.Model.Requests;

/// <summary>
/// Login credentials, always read from the POST body - never a query string
/// (rulebook §5).
/// </summary>
public class LoginRequest
{
    public string Email { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}
