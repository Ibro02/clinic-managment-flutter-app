using ClinicNow.Model.Requests;

namespace ClinicNow.Services.Notifications;

/// <summary>
/// Manages which devices a user receives push notifications on. Not a CRUD
/// service: there is no list, no paging and no read side at all - a device token
/// is an opaque routing detail, and exposing "which devices does this account
/// use" would be a privacy surface with no feature behind it.
/// </summary>
public interface IDeviceTokenService
{
    /// <summary>
    /// Registers (or re-registers) the calling user's device. Idempotent: the
    /// client calls this on every login and on every FCM token refresh, so the
    /// normal case is a token that already exists.
    /// </summary>
    Task RegisterAsync(RegisterDeviceTokenRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a device on sign-out, so the next person to use it does not
    /// receive the previous user's notifications. Silent when the token is
    /// already gone - a logout that races a token refresh is not an error.
    /// </summary>
    Task UnregisterAsync(UnregisterDeviceTokenRequest request, CancellationToken cancellationToken = default);
}
