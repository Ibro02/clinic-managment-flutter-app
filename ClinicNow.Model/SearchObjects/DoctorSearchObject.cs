namespace ClinicNow.Model.SearchObjects;

public class DoctorSearchObject : BaseSearchObject
{
    /// <summary>Case-insensitive partial match against first name + last name.</summary>
    public string? Name { get; set; }

    /// <summary>Only doctors with this specialization.</summary>
    public int? SpecializationId { get; set; }

    /// <inheritdoc />
    /// <remarks>
    /// <c>User.LastName</c> is a deliberate, single explicit exception to
    /// "flat properties only" (every other search object's allowlist has no
    /// navigation paths): a doctor's own name lives on the linked
    /// <see cref="ClinicNow.Services.Database.Entities.User"/>, not on
    /// <c>Doctor</c> itself, and staff/patients need to sort the doctor list
    /// by name. Allowlisting exactly this one safe, non-sensitive path is
    /// what keeps <c>OrderBy(string)</c> from also accepting something like
    /// <c>User.PasswordHash</c> - the whole reason this allowlist exists
    /// (see <see cref="ClinicNow.Services.BaseService{TModel,TSearch,TDbEntity}.ApplySorting"/>).
    /// </remarks>
    protected override ISet<string> SortableColumns { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "Id", "LicenseNumber", "CreatedAtUtc", "User.LastName" };
}
