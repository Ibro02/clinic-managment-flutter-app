using ClinicNow.Model.Dto;
using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;

namespace ClinicNow.Services.Codebooks;

public class LocationService
    : BaseCRUDService<LocationDto, LocationSearchObject, Location, LocationInsertRequest, LocationUpdateRequest>
{
    public LocationService(ClinicNowContext context, IMapper mapper) : base(context, mapper)
    {
    }

    protected override IQueryable<Location> ApplyFilter(LocationSearchObject search, IQueryable<Location> query)
    {
        // City must be loaded for LocationDto.CityName (rulebook Part II §K: never
        // show raw IDs) - ApplyFilter runs on both the count and the data path, so
        // this is the one place that covers GetPagedAsync entirely.
        query = query.Include(l => l.City);

        if (!string.IsNullOrWhiteSpace(search.Name))
        {
            query = query.Where(l => l.Name.Contains(search.Name));
        }

        if (search.CityId.HasValue)
        {
            query = query.Where(l => l.CityId == search.CityId.Value);
        }

        return query;
    }

    public override async Task<LocationDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        // BaseService.GetByIdAsync uses FindAsync, which doesn't support Include -
        // overridden here so a direct GET by ID also returns a populated CityName.
        var entity = await Context.Locations
            .Include(l => l.City)
            .SingleOrDefaultAsync(l => l.Id == id, cancellationToken);

        return entity is null ? null : Mapper.Map<LocationDto>(entity);
    }

    protected override async Task BeforeInsertAsync(LocationInsertRequest request, Location entity, CancellationToken cancellationToken)
    {
        ValidateNameAndAddress(entity.Name, entity.Address);
        await EnsureCityExistsAsync(request.CityId, cancellationToken);
    }

    protected override async Task BeforeUpdateAsync(LocationUpdateRequest request, Location entity, CancellationToken cancellationToken)
    {
        ValidateNameAndAddress(request.Name, request.Address);
        await EnsureCityExistsAsync(request.CityId, cancellationToken);
    }

    // Base InsertAsync/UpdateAsync map the response DTO from `entity` right after
    // these hooks run (before any query re-fetch) - loading City here is what
    // makes CityName populated on the very first response after create/edit,
    // not just on the next list refresh.
    protected override Task AfterInsertAsync(LocationInsertRequest request, Location entity, CancellationToken cancellationToken) =>
        Context.Entry(entity).Reference(l => l.City).LoadAsync(cancellationToken);

    protected override Task AfterUpdateAsync(LocationUpdateRequest request, Location entity, CancellationToken cancellationToken) =>
        Context.Entry(entity).Reference(l => l.City).LoadAsync(cancellationToken);

    private static void ValidateNameAndAddress(string name, string address)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(name))
        {
            errors["name"] = ["Naziv lokacije je obavezan."];
        }

        if (string.IsNullOrWhiteSpace(address))
        {
            errors["address"] = ["Adresa je obavezna."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    private async Task EnsureCityExistsAsync(int cityId, CancellationToken cancellationToken)
    {
        var exists = await Context.Cities.AnyAsync(c => c.Id == cityId, cancellationToken);
        if (!exists)
        {
            throw new ValidationException("cityId", "Odabrani grad ne postoji.");
        }
    }
}
