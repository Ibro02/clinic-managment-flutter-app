namespace ClinicNow.API;

/// <summary>
/// Names of the rate-limiter policies configured in <c>Program.cs</c>. A static
/// class of constants, per the same pattern as <c>ClinicNow.Model.Security.Roles</c>
/// - no magic strings scattered across controllers (rulebook Part II §D).
/// </summary>
public static class RateLimiterPolicies
{
    /// <summary>Applied to login/register: brute-force and account-enumeration protection (global rule "Add rate limiting on auth and write operations").</summary>
    public const string Auth = "auth";
}
