namespace ClinicNow.Model.SearchObjects;

public class ScheduleBlockSearchObject : BaseSearchObject
{
    public int? DoctorId { get; set; }

    /// <inheritdoc />
    protected override ISet<string> SortableColumns { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "Id", "StartUtc", "EndUtc" };
}
