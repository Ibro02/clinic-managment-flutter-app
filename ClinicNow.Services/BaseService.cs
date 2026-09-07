using ClinicNow.Model.Exceptions;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Services.Database;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using System.Linq.Dynamic.Core;
// ClinicNow.Model.Common is deliberately not `using`-imported here: it defines its
// own PagedResult<T>, which collides with System.Linq.Dynamic.Core.PagedResult<T>
// (used internally by the OrderBy(string) extension below). Referenced fully
// qualified instead so both packages' types stay unambiguous.

namespace ClinicNow.Services;

/// <summary>
/// Generic read-only service: paging, an entity-specific filter hook, and dynamic
/// sorting - shared by every entity in the system. Concrete services extend this (or
/// <see cref="BaseCRUDService{TModel,TSearch,TDbEntity,TInsert,TUpdate}"/> for
/// writes) and override <see cref="ApplyFilter"/> for their own search behaviour.
///
/// Registered as <c>Scoped</c> in DI (it holds a <see cref="ClinicNowContext"/>) -
/// never <c>Transient</c>, per rulebook Part II §D.
/// </summary>
public abstract class BaseService<TModel, TSearch, TDbEntity> : IService<TModel, TSearch>
    where TSearch : BaseSearchObject
    where TDbEntity : class
    where TModel : class
{
    protected readonly ClinicNowContext Context;
    protected readonly IMapper Mapper;

    protected BaseService(ClinicNowContext context, IMapper mapper)
    {
        Context = context;
        Mapper = mapper;
    }

    public virtual async Task<ClinicNow.Model.Common.PagedResult<TModel>> GetPagedAsync(TSearch search, CancellationToken cancellationToken = default)
    {
        var query = Context.Set<TDbEntity>().AsQueryable();
        query = ApplyFilter(search, query);

        // Count before paging (and after filtering) so clients can render page controls.
        var count = await query.CountAsync(cancellationToken);

        query = ApplySorting(search, query);

        query = query
            .Skip((search.Page - 1) * search.PageSize)
            .Take(search.PageSize); // PageSize is already capped by BaseSearchObject.

        // AsNoTracking: this is a read path by construction - nothing here is ever
        // mutated and saved - so there is no reason to pay for a change-tracker
        // snapshot per entity plus O(n) identity-map fixup on every list request.
        //
        // Plain AsNoTracking is correct *here* because this generic query never
        // uses AsSplitQuery. A service that does (see AppointmentService.IncludeAll)
        // must use AsNoTrackingWithIdentityResolution instead, or the same related
        // row materializes as separate instances across the split result sets.
        var entities = await query.AsNoTracking().ToListAsync(cancellationToken);

        return new ClinicNow.Model.Common.PagedResult<TModel>
        {
            Count = count,
            ResultList = Mapper.Map<List<TModel>>(entities)
        };
    }

    public virtual async Task<TModel?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await Context.Set<TDbEntity>().FindAsync([id], cancellationToken);
        return entity is null ? null : Mapper.Map<TModel>(entity);
    }

    /// <summary>
    /// Entity-specific WHERE clauses, applied against the database (never against an
    /// in-memory list - rulebook Part II §D: "Filtriranje... treba raditi na nivou
    /// baze"). Base implementation is a no-op; override per entity.
    /// </summary>
    protected virtual IQueryable<TDbEntity> ApplyFilter(TSearch search, IQueryable<TDbEntity> query) => query;

    /// <summary>
    /// Applies <see cref="BaseSearchObject.OrderBy"/>/<see cref="BaseSearchObject.SortDirection"/>
    /// via System.Linq.Dynamic.Core, restricted to the columns the search object
    /// declares sortable.
    ///
    /// The allowlist is the whole point: <c>OrderBy(string)</c> will happily accept
    /// any property on the entity and any navigation path off it, so an unrestricted
    /// <c>?orderBy=</c> lets a caller sort by a column no DTO exposes
    /// (<c>Doctor.User.PasswordHash</c>) and read it back out of the resulting row
    /// order one comparison at a time.
    ///
    /// A rejected column throws rather than being silently ignored. Ignoring it hides
    /// a genuine client bug behind seemingly-working output, and - worse - makes an
    /// invalid probe indistinguishable from a valid one, which is exactly the free
    /// property-name oracle an attacker wants.
    /// </summary>
    protected static IQueryable<TDbEntity> ApplySorting(TSearch search, IQueryable<TDbEntity> query)
    {
        if (string.IsNullOrWhiteSpace(search.OrderBy))
        {
            return query;
        }

        if (!search.IsSortable(search.OrderBy))
        {
            throw new ValidationException("orderBy",
                $"Sortiranje po koloni '{search.OrderBy}' nije dozvoljeno.");
        }

        var direction = (search.SortDirection ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "desc" or "descending" => "descending",
            _ => "ascending"
        };

        return query.OrderBy($"{search.OrderBy} {direction}");
    }
}
