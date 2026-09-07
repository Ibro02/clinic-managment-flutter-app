namespace ClinicNow.Model.SearchObjects;

public class NotificationSearchObject : BaseSearchObject
{
    /// <summary>When set, filters to only read (true) or only unread (false) notifications.</summary>
    public bool? IsRead { get; set; }

    /// <inheritdoc />
    protected override ISet<string> SortableColumns { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "Id", "IsRead", "CreatedAtUtc", "ReadAtUtc" };
}
