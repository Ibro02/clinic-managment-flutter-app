namespace ClinicNow.Model.Dto;

/// <summary>Successful login/register response: the bearer token plus the caller's own profile.</summary>
public class LoginResponseDto
{
    public string AccessToken { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }

    public UserDto User { get; set; } = null!;
}
