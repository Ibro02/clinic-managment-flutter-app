using ClinicNow.Model.Dto;
using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;

namespace ClinicNow.Services.Codebooks;

/// <summary>
/// Registered directly against the generic <c>ICRUDService&lt;...&gt;</c> in
/// Program.cs - no entity-specific interface needed since City has no behaviour
/// beyond plain CRUD (rulebook Part II §D: reuse the generic base classes).
/// </summary>
public class CityService : BaseCRUDService<CityDto, CitySearchObject, City, CityInsertRequest, CityUpdateRequest>
{
    public CityService(ClinicNowContext context, IMapper mapper) : base(context, mapper)
    {
    }

    protected override IQueryable<City> ApplyFilter(CitySearchObject search, IQueryable<City> query)
    {
        if (!string.IsNullOrWhiteSpace(search.Name))
        {
            query = query.Where(c => c.Name.Contains(search.Name));
        }

        return query;
    }

    protected override async Task BeforeInsertAsync(CityInsertRequest request, City entity, CancellationToken cancellationToken)
    {
        entity.Name = NormalizeAndValidateName(request.Name);
        await EnsureNameIsUniqueAsync(entity.Name, excludeId: null, cancellationToken);
    }

    protected override async Task BeforeUpdateAsync(CityUpdateRequest request, City entity, CancellationToken cancellationToken)
    {
        var normalizedName = NormalizeAndValidateName(request.Name);
        await EnsureNameIsUniqueAsync(normalizedName, excludeId: entity.Id, cancellationToken);
        request.Name = normalizedName;
    }

    private static string NormalizeAndValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException("name", "Naziv grada je obavezan.");
        }

        return name.Trim();
    }

    private async Task EnsureNameIsUniqueAsync(string name, int? excludeId, CancellationToken cancellationToken)
    {
        var exists = await Context.Cities.AnyAsync(
            c => c.Name == name && c.Id != (excludeId ?? 0), cancellationToken);

        if (exists)
        {
            throw new ValidationException("name", $"Grad sa nazivom '{name}' već postoji.");
        }
    }

    protected override async Task BeforeDeleteAsync(City entity, CancellationToken cancellationToken)
    {
        var hasLocations = await Context.Locations.AnyAsync(l => l.CityId == entity.Id, cancellationToken);
        if (hasLocations)
        {
            throw new BusinessException(
                $"Grad '{entity.Name}' se ne može obrisati jer postoje lokacije koje ga koriste.");
        }
    }
}
