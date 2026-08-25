using ClinicNow.Model.Dto;
using ClinicNow.Model.Requests;

namespace ClinicNow.Services.Users;

/// <summary>Registration, login, logout, and "who am I" for the current bearer token.</summary>
public interface IUserService
{
    /// <summary>
    /// Self-registers a new <c>Patient</c> account. The role is never taken from
    /// <paramref name="request"/> - self-registration is always Patient (rulebook §5).
    /// </summary>
    Task<UserDto> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);

    Task<LoginResponseDto> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    /// <summary>Revokes the bearer token that authenticated the current request.</summary>
    Task LogoutAsync(CancellationToken cancellationToken = default);

    /// <summary>Profile of the user identified by the current request's bearer token.</summary>
    Task<UserDto> GetCurrentUserAsync(CancellationToken cancellationToken = default);
}
