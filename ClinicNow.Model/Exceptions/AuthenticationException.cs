namespace ClinicNow.Model.Exceptions;

/// <summary>
/// Login credentials were invalid, or the account is disabled. Maps to HTTP 401
/// Unauthorized (as opposed to <see cref="BusinessException"/>'s 400) so Flutter
/// clients can distinguish "wrong credentials, stay on the login form" from a
/// generic validation failure, and so an expired/invalid bearer token on any other
/// endpoint gets the same, correctly-handled status code the client already reacts
/// to by redirecting to login.
/// </summary>
public class AuthenticationException : ClinicNowException
{
    public AuthenticationException(string message) : base(message)
    {
    }
}
