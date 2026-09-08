namespace ClinicNow.Model.Requests;

/// <summary>
/// Edits the signed-in user's own profile (review item C7). Carries no user
/// id on purpose: the identity comes from the JWT, never from the body, so a
/// patient cannot rename somebody else by editing a field (rulebook Part II §F).
///
/// Email is deliberately absent. It is the login identity and is mirrored onto
/// the patient's medical record, so changing it is an account operation rather
/// than a profile edit - and the rulebook (§E) is explicit that an edit form
/// must not force the user through fields they did not come to change.
/// </summary>
public class UpdateProfileRequest
{
    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }

    /// <summary>Whether to receive the pre-appointment reminder by email.</summary>
    public bool EmailRemindersEnabled { get; set; }

    /// <summary>
    /// The mobile app's "jezik aplikacije" preference (review item 7) - one of
    /// <see cref="Localization.PatientLanguage.Supported"/>. Validated and
    /// normalized server-side; never trust the client to only ever send a
    /// supported value.
    /// </summary>
    public string PreferredLanguage { get; set; } = Localization.PatientLanguage.Bosnian;
}

/// <summary>
/// Changing your own password, which requires proving you know the current one
/// (rulebook §E). An administrator resetting somebody else's password is a
/// different flow and does not go through here.
/// </summary>
public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;

    public string NewPassword { get; set; } = string.Empty;

    public string ConfirmNewPassword { get; set; } = string.Empty;
}

/// <summary>Step one of a forgotten-password reset: ask for a code by email.</summary>
public class ForgotPasswordRequest
{
    public string Email { get; set; } = string.Empty;
}

/// <summary>Step two: redeem the emailed code for a new password.</summary>
public class ResetPasswordRequest
{
    public string Email { get; set; } = string.Empty;

    /// <summary>The code from the email. Single-use and short-lived.</summary>
    public string Code { get; set; } = string.Empty;

    public string NewPassword { get; set; } = string.Empty;

    public string ConfirmNewPassword { get; set; } = string.Empty;
}
