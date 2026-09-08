namespace ClinicNow.Model.SearchObjects;

/// <summary>Lists <c>User</c> rows with the Staff role (review item: the admin
/// user list was missing Staff accounts entirely - there was no screen or
/// endpoint for them at all).</summary>
public class StaffSearchObject : BaseSearchObject
{
    /// <summary>Case-insensitive partial match against first name + last name.</summary>
    public string? Name { get; set; }

    /// <inheritdoc />
    protected override ISet<string> SortableColumns { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "Id", "FirstName", "LastName", "Email", "CreatedAtUtc" };
}
