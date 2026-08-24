namespace ClinicNow.Model.Common;

/// <summary>
/// Envelope returned by every list endpoint: the page of results plus the total
/// row count matching the filter (needed by clients to render pagination controls).
/// List endpoints must never return unbounded data - see <see cref="ClinicNow.Model.SearchObjects.BaseSearchObject"/>.
/// </summary>
public class PagedResult<T>
{
    public List<T> ResultList { get; set; } = [];

    /// <summary>Total number of rows matching the filter, ignoring paging.</summary>
    public int Count { get; set; }
}
