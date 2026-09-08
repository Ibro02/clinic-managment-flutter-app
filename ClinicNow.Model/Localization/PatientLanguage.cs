namespace ClinicNow.Model.Localization;

/// <summary>
/// The mobile app's "jezik aplikacije" preference (prijava/review item 7 - kept
/// from the submitted topic proposal, so it must have a real backend effect, not
/// just a UI control). Only two values are supported, matching what the
/// registration promised: Bosnian (the clinic's default) and English.
///
/// Both the constant <em>names</em> and their string <em>values</em> are English,
/// same reasoning as <see cref="Security.Roles"/>: this is a system contract
/// (stored value, JSON field name) rather than user-facing content.
/// </summary>
public static class PatientLanguage
{
    public const string Bosnian = "bs";

    public const string English = "en";

    public static readonly IReadOnlyList<string> Supported = [Bosnian, English];

    /// <summary>Whether <paramref name="language"/> is one of the two supported values.</summary>
    public static bool IsSupported(string? language) => Supported.Contains(language);

    /// <summary>
    /// Falls back to <see cref="Bosnian"/> for anything unrecognized (null, a
    /// stale value from before a language was ever removed, ...) - message
    /// rendering must never throw over a bad preference value.
    /// </summary>
    public static string Normalize(string? language) => IsSupported(language) ? language! : Bosnian;
}
