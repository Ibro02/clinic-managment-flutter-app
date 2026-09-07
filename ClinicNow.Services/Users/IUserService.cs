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

    /// <summary>
    /// Edits the profile of the user identified by the current bearer token
    /// (review item C7). The id is never taken from the request.
    /// </summary>
    Task<UserDto> UpdateCurrentUserAsync(UpdateProfileRequest request, CancellationToken cancellationToken = default);

    /// <summary>Changes the current user's own password, after verifying the current one.</summary>
    Task ChangeCurrentUserPasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Issues a single-use, short-lived reset code and emails it. Deliberately
    /// returns nothing and reports success even for an address with no account:
    /// a caller must not be able to use this endpoint to discover which emails
    /// are registered.
    /// </summary>
    Task RequestPasswordResetAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default);

    /// <summary>Redeems a reset code for a new password.</summary>
    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);
}
