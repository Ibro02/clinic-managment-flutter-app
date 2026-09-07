namespace ClinicNow.Model.SearchObjects;

public class CitySearchObject : BaseSearchObject
{
    /// <summary>Case-insensitive partial match on <c>Name</c>.</summary>
    public string? Name { get; set; }

    /// <inheritdoc />
    protected override ISet<string> SortableColumns { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "Id", "Name" };
}
