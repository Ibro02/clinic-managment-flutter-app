using System.Text.RegularExpressions;

namespace ClinicNow.Services.Validation;

/// <summary>
/// The single definition of what a valid email, phone number and free-text
/// length looks like (review item C17). Every flow that fills the same column
/// asks the same question here, so registration, staff-created patients and
/// admin-created doctors can no longer disagree about what they accept.
///
/// Before this, they did: registration matched a real email pattern and a phone
/// pattern, <c>PatientService</c> accepted anything containing an "@" and never
/// looked at the phone at all, and <c>DoctorService</c> checked email
/// *uniqueness* with no format check whatsoever - so the same address was
/// rejected on one screen and stored on another.
///
/// The length limits mirror the EF configuration for the columns these values
/// land in. Without them an over-long value passes service validation and then
/// fails deep in SQL as a truncation error, which reaches the user as an
/// unexplained 500 instead of a message under the field (rulebook §4).
///
/// Same reasoning as <see cref="ClinicNow.Services.Documents.FileValidation"/>
/// and <see cref="ClinicNow.Services.Appointments.DoctorCompatibility"/>: one
/// rule, many callers, never a second copy.
/// </summary>
public static partial class ContactRules
{
    /// <summary>
    /// Matches <c>UserConfiguration</c>'s <c>Email</c> column. <c>Patient.Email</c>
    /// is configured wider (320), but a patient's address may later become a
    /// login, so the stricter of the two is the shared bound - anything that
    /// passes here fits both columns.
    /// </summary>
    public const int MaxEmailLength = 256;

    public const int MaxPhoneLength = 30;   // User.PhoneNumber / Patient.PhoneNumber
    public const int MaxNameLength = 100;   // First/LastName on both User and Patient
    public const int MaxPersonalIdLength = 20;
    public const int MaxAddressLength = 250;
    public const int MaxLicenseNumberLength = 50;  // Doctor.LicenseNumber
    public const int MaxBioLength = 1000;          // Doctor.Bio
    public const int MinPasswordLength = 8;

    // Messages name the expected format rather than just saying "invalid"
    // (rulebook §4: the message states the constraint).
    public const string EmailMessage = "Unesite ispravnu email adresu (npr. ime@primjer.com).";
    public const string PhoneMessage = "Unesite ispravan broj telefona (npr. +38761123456).";

    /// <summary>Required email: must be present, well-formed and within the column length.</summary>
    public static void RequireEmail(IDictionary<string, string[]> errors, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !EmailPattern().IsMatch(value.Trim()))
        {
            errors[field] = [EmailMessage];
            return;
        }

        CheckLength(errors, field, value, MaxEmailLength, "Email adresa");
    }

    /// <summary>Optional email: blank is fine, but anything supplied is held to the same rule.</summary>
    public static void OptionalEmail(IDictionary<string, string[]> errors, string field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            RequireEmail(errors, field, value);
        }
    }

    /// <summary>Optional phone - no flow here makes one mandatory, but a supplied number must be real.</summary>
    public static void OptionalPhone(IDictionary<string, string[]> errors, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!PhonePattern().IsMatch(value.Trim()))
        {
            errors[field] = [PhoneMessage];
            return;
        }

        CheckLength(errors, field, value, MaxPhoneLength, "Broj telefona");
    }

    /// <summary>
    /// Required free text (a name): present, and short enough for its column.
    /// The "je obavezno" phrasing matches the labels that actually use this
    /// today (Ime, Prezime - both neuter) and the wording the Flutter forms
    /// already show; a feminine label would need its own message rather than
    /// this template.
    /// </summary>
    public static void RequireText(
        IDictionary<string, string[]> errors, string field, string? value, int maxLength, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[field] = [$"{label} je obavezno."];
            return;
        }

        CheckLength(errors, field, value, maxLength, label);
    }

    /// <summary>Optional free text: only the length matters.</summary>
    public static void OptionalText(
        IDictionary<string, string[]> errors, string field, string? value, int maxLength, string label)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            CheckLength(errors, field, value, maxLength, label);
        }
    }

    public static void RequirePassword(IDictionary<string, string[]> errors, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < MinPasswordLength)
        {
            errors[field] = [$"Lozinka mora imati najmanje {MinPasswordLength} karaktera."];
        }
    }

    private static void CheckLength(
        IDictionary<string, string[]> errors, string field, string value, int maxLength, string label)
    {
        // Trimmed, because every caller trims before persisting - validating the
        // untrimmed value would reject input that would actually have fit.
        if (value.Trim().Length > maxLength)
        {
            errors[field] = [$"{label} može imati najviše {maxLength} karaktera."];
        }
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"^\+?[0-9 ]{6,20}$")]
    private static partial Regex PhonePattern();
}
