namespace ClinicNow.Model.SearchObjects;

/// <summary>
/// Base query parameters for every list endpoint: paging + sorting.
///
/// Per the RS II 2025/26 rulebook (Part II §D): "Paginacija je obavezna na svakom
/// list endpointu. PageSize mora imati definisan maksimalni limit... Endpointi tipa
/// RetrieveAll bez limita smatraju se greskom." <see cref="PageSize"/> therefore
/// clamps itself to <see cref="MaxPageSize"/> unconditionally - there is no
/// "retrieve all" escape hatch anywhere in this hierarchy, by design.
/// </summary>
public abstract class BaseSearchObject
{
    public const int DefaultPageSize = 10;
    public const int MaxPageSize = 100;

    private int _page = 1;
    private int _pageSize = DefaultPageSize;

    /// <summary>1-based page number. Values below 1 are clamped to 1.</summary>
    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    /// <summary>
    /// Rows per page. Clamped to [1, <see cref="MaxPageSize"/>] no matter what the
    /// caller requests - this is the enforcement point for the rulebook's mandatory
    /// page-size cap, so no individual service/controller can accidentally bypass it.
    /// </summary>
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value switch
        {
            <= 0 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => value
        };
    }

    /// <summary>Name of the entity property to sort by. Ignored if unknown/absent.</summary>
    public string? OrderBy { get; set; }

    /// <summary>"asc"/"ascending" or "desc"/"descending". Ignored if absent/invalid.</summary>
    public string? SortDirection { get; set; }
}
