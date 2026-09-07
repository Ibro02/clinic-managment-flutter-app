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

    /// <summary>Name of the entity property to sort by. Must be one of <see cref="SortableColumns"/>.</summary>
    public string? OrderBy { get; set; }

    /// <summary>"asc"/"ascending" or "desc"/"descending". Ignored if absent/invalid.</summary>
    public string? SortDirection { get; set; }

    /// <summary>
    /// The exact set of property names this search object permits in
    /// <see cref="OrderBy"/>. Every derived search object declares its own.
    ///
    /// This exists because <c>OrderBy</c> is fed to System.Linq.Dynamic.Core's
    /// <c>OrderBy(string)</c>, which accepts *any* property on the entity and any
    /// navigation path off it. Without a closed allowlist a caller could sort by a
    /// column no DTO ever exposes - <c>Doctor.User.PasswordHash</c>,
    /// <c>User.PasswordResetTokenHash</c> - and read the hidden value back out one
    /// comparison at a time, since the resulting row order *is* an oracle over that
    /// column. Deep navigation paths are also an easy way to force arbitrarily
    /// expensive joins.
    ///
    /// Getter-only and <c>protected</c> on purpose: the ASP.NET Core query-string
    /// model binder only binds settable public properties, so this can never be
    /// supplied by the client. Callers ask <see cref="IsSortable"/> instead.
    /// </summary>
    protected virtual ISet<string> SortableColumns { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Id" };

    /// <summary>
    /// Whether <paramref name="column"/> may be used in an ORDER BY for this search.
    /// Case-insensitive, since query strings are written by hand.
    /// </summary>
    public bool IsSortable(string? column) =>
        !string.IsNullOrWhiteSpace(column) && SortableColumns.Contains(column);
}
