namespace ClinicNow.Model.SearchObjects;

public class NewsItemSearchObject : BaseSearchObject
{
    /// <summary>Case-insensitive partial match against the title.</summary>
    public string? Title { get; set; }

    /// <inheritdoc />
    protected override ISet<string> SortableColumns { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "Id", "Title", "CreatedAtUtc" };
}
