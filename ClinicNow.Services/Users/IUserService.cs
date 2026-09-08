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
    /// <summary>
    /// Changes the caller's own password and returns a replacement access token.
    /// The change invalidates every token issued before it (including the one used
    /// to make this call), so a fresh one is handed back to keep the caller signed
    /// in while every *other* session is ended.
    /// </summary>
    Task<LoginResponseDto> ChangeCurrentUserPasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Issues a single-use, short-lived reset code and emails it. Deliberately
    /// returns nothing and reports success even for an address with no account:
    /// a caller must not be able to use this endpoint to discover which emails
    /// are registered.
    /// </summary>
    Task RequestPasswordResetAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default);

    /// <summary>Redeems a reset code for a new password.</summary>
    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Administrator-only: changes any account's login email by id, including
    /// the administrator's own. The one place email becomes editable at all -
    /// <see cref="UpdateCurrentUserAsync"/> deliberately never touches it.
    /// Role-gated on the controller, not here; <paramref name="id"/> is taken
    /// from the route because unlike the self-service endpoints, the whole
    /// point is acting on an account that need not be the caller's.
    /// </summary>
    Task<UserDto> AdminUpdateEmailAsync(int id, AdminUpdateEmailRequest request, CancellationToken cancellationToken = default);
}
