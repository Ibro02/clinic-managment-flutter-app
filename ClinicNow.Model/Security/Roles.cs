namespace ClinicNow.Model.Security;

/// <summary>
/// Canonical role name constants. Used in <c>[Authorize(Roles = ...)]</c> attributes,
/// seed data, and JWT role claims - a single source of truth so the seeded role names
/// can never drift from the strings checked by authorization attributes (rulebook §5:
/// seed role names must match the names used in the attributes).
/// Never use raw role strings anywhere else in the codebase.
///
/// Both the constant *names* and their string *values* are English: role names are
/// part of the auth/system contract (checked against seed data and JWT claims), not
/// user-facing content, so the English-code rule applies to the values too - contrast
/// with e.g. seeded specialization/service names, which stay in Bosnian because a
/// patient actually reads them. See CLAUDE.md "Language".
/// </summary>
public static class Roles
{
    /// <summary>Full administrative access: manages users, codebooks, and all clinic data.</summary>
    public const string Administrator = "Administrator";

    /// <summary>Clinic front-desk/administrative staff: patients, appointments, documents.</summary>
    public const string Staff = "Staff";

    /// <summary>A doctor: own schedule, own appointments, own patients' documentation.</summary>
    public const string Doctor = "Doctor";

    /// <summary>A patient (mobile app): own appointments, own documents, own profile.</summary>
    public const string Patient = "Patient";

    /// <summary>All known roles, e.g. for seeding the Role reference table.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Administrator,
        Staff,
        Doctor,
        Patient
    ];
}
