namespace ClinicNow.Model.SearchObjects;

public class LabFindingSearchObject : BaseSearchObject
{
    /// <summary>Scopes the list to one patient's findings (a karton/documentation view). Ignored/forced for the Patient role - see <c>LabFindingService</c>.</summary>
    public int? PatientId { get; set; }

    /// <summary>Scopes the list to one appointment's findings (the desktop entry/review flow, review item C4).</summary>
    public int? AppointmentId { get; set; }

    /// <summary>Case-insensitive partial match against the finding's result text or file name (rulebook §2.2: every list needs at least one search parameter).</summary>
    public string? Search { get; set; }

    /// <inheritdoc />
    protected override ISet<string> SortableColumns { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "Id", "FileName", "FileSizeBytes", "CreatedAtUtc" };
}
