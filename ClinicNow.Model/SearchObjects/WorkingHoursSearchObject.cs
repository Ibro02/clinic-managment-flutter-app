namespace ClinicNow.Model.SearchObjects;

public class WorkingHoursSearchObject : BaseSearchObject
{
    public int? DoctorId { get; set; }

    /// <inheritdoc />
    protected override ISet<string> SortableColumns { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "Id", "DayOfWeek", "StartTime", "EndTime" };
}
