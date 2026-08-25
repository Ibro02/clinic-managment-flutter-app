using ClinicNow.Model.Dto;
using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Services.Database;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using DbMedicalService = ClinicNow.Services.Database.Entities.MedicalService;

namespace ClinicNow.Services.Codebooks;

/// <summary>
/// The DB entity is aliased to <see cref="DbMedicalService"/> purely to avoid a
/// same-name collision with this class's own namespace segment
/// (<c>ClinicNow.Services.Codebooks.MedicalServiceService</c> vs.
/// <c>ClinicNow.Services.Database.Entities.MedicalService</c>) - both are still
/// plain, unambiguous types at runtime.
/// </summary>
public class MedicalServiceService
    : BaseCRUDService<MedicalServiceDto, MedicalServiceSearchObject, DbMedicalService, MedicalServiceInsertRequest, MedicalServiceUpdateRequest>
{
    public MedicalServiceService(ClinicNowContext context, IMapper mapper) : base(context, mapper)
    {
    }

    protected override IQueryable<DbMedicalService> ApplyFilter(MedicalServiceSearchObject search, IQueryable<DbMedicalService> query)
    {
        if (!string.IsNullOrWhiteSpace(search.Name))
        {
            query = query.Where(s => s.Name.Contains(search.Name));
        }

        return query;
    }

    protected override async Task BeforeInsertAsync(MedicalServiceInsertRequest request, DbMedicalService entity, CancellationToken cancellationToken)
    {
        Validate(request.Name, request.Price, request.DurationMinutes);
        await EnsureNameIsUniqueAsync(request.Name.Trim(), excludeId: null, cancellationToken);
        entity.Name = request.Name.Trim();
    }

    protected override async Task BeforeUpdateAsync(MedicalServiceUpdateRequest request, DbMedicalService entity, CancellationToken cancellationToken)
    {
        Validate(request.Name, request.Price, request.DurationMinutes);
        await EnsureNameIsUniqueAsync(request.Name.Trim(), excludeId: entity.Id, cancellationToken);
        request.Name = request.Name.Trim();
    }

    private static void Validate(string name, decimal price, int durationMinutes)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(name))
        {
            errors["name"] = ["Naziv usluge je obavezan."];
        }

        if (price < 0)
        {
            errors["price"] = ["Cijena ne može biti negativna."];
        }

        if (durationMinutes <= 0)
        {
            errors["durationMinutes"] = ["Trajanje usluge mora biti veće od 0 minuta."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    private async Task EnsureNameIsUniqueAsync(string name, int? excludeId, CancellationToken cancellationToken)
    {
        var exists = await Context.MedicalServices.AnyAsync(
            s => s.Name == name && s.Id != (excludeId ?? 0), cancellationToken);

        if (exists)
        {
            throw new ValidationException("name", $"Usluga sa nazivom '{name}' već postoji.");
        }
    }
}
