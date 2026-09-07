namespace ClinicNow.Model.SearchObjects;

public class MedicalServiceSearchObject : BaseSearchObject
{
    /// <summary>Case-insensitive partial match on <c>Name</c>.</summary>
    public string? Name { get; set; }

    /// <summary>Only services belonging to this specialization.</summary>
    public int? SpecializationId { get; set; }

    /// <summary>
    /// Only services this doctor is qualified to perform - i.e. whose
    /// specialization the doctor holds. This is what the booking screens filter on,
    /// so the dropdown offers exactly what the server would accept (review item C2).
    /// </summary>
    public int? DoctorId { get; set; }

    /// <inheritdoc />
    protected override ISet<string> SortableColumns { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "Id", "Name", "Price", "DurationMinutes" };
}
