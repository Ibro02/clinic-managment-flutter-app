namespace ClinicNow.Model.SearchObjects;

public class LocationSearchObject : BaseSearchObject
{
    /// <summary>Case-insensitive partial match on <c>Name</c>.</summary>
    public string? Name { get; set; }

    /// <summary>Exact filter - locations in this city only.</summary>
    public int? CityId { get; set; }

    /// <inheritdoc />
    protected override ISet<string> SortableColumns { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "Id", "Name", "Address" };
}
