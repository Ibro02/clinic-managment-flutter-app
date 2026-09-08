namespace ClinicNow.Model.Requests;

/// <summary>
/// Creates a Staff (front-desk/administrative) login account. Staff has no
/// profile fields beyond <c>User</c> itself - unlike Doctor/Patient there is no
/// separate domain entity, so this mirrors <see cref="DoctorInsertRequest"/>
/// minus the doctor-only fields. The server assigns the Staff role; it is
/// never taken from the client (rulebook §5).
/// </summary>
public class StaffInsertRequest
{
    public string Email { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }
}

/// <summary>Updates name/phone on the linked User. Never changes email/password - see AuthController's admin email endpoint and rulebook Part II §E for those separate flows.</summary>
public class StaffUpdateRequest
{
    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }
}
