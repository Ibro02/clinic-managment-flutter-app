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
        // Needed for MedicalServiceDto.SpecializationName (CodebookMappingConfig).
        query = query.Include(s => s.Specialization);

        if (!string.IsNullOrWhiteSpace(search.Name))
        {
            query = query.Where(s => s.Name.Contains(search.Name));
        }

        if (search.SpecializationId.HasValue)
        {
            query = query.Where(s => s.SpecializationId == search.SpecializationId.Value);
        }

        // "Which services can this doctor perform" - the booking screens filter on
        // this so the dropdown offers exactly what the server would accept
        // (review item C2). Expressed as a subquery, not an in-memory join.
        if (search.DoctorId.HasValue)
        {
            query = query.Where(s => Context.DoctorSpecializations
                .Any(ds => ds.DoctorId == search.DoctorId.Value && ds.SpecializationId == s.SpecializationId));
        }

        return query;
    }

    public override async Task<MedicalServiceDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await Context.MedicalServices
            .Include(s => s.Specialization)
            .SingleOrDefaultAsync(s => s.Id == id, cancellationToken);

        return entity is null ? null : Mapper.Map<MedicalServiceDto>(entity);
    }

    protected override async Task BeforeInsertAsync(MedicalServiceInsertRequest request, DbMedicalService entity, CancellationToken cancellationToken)
    {
        Validate(request.Name, request.Price, request.DurationMinutes);
        await EnsureNameIsUniqueAsync(request.Name.Trim(), excludeId: null, cancellationToken);
        await EnsureSpecializationExistsAsync(request.SpecializationId, cancellationToken);
        entity.Name = request.Name.Trim();
    }

    protected override async Task AfterInsertAsync(MedicalServiceInsertRequest request, DbMedicalService entity, CancellationToken cancellationToken) =>
        await Context.Entry(entity).Reference(s => s.Specialization).LoadAsync(cancellationToken);

    protected override async Task BeforeUpdateAsync(MedicalServiceUpdateRequest request, DbMedicalService entity, CancellationToken cancellationToken)
    {
        Validate(request.Name, request.Price, request.DurationMinutes);
        await EnsureNameIsUniqueAsync(request.Name.Trim(), excludeId: entity.Id, cancellationToken);
        await EnsureSpecializationExistsAsync(request.SpecializationId, cancellationToken);
        request.Name = request.Name.Trim();
    }

    protected override async Task AfterUpdateAsync(MedicalServiceUpdateRequest request, DbMedicalService entity, CancellationToken cancellationToken) =>
        await Context.Entry(entity).Reference(s => s.Specialization).LoadAsync(cancellationToken);

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

    private async Task EnsureSpecializationExistsAsync(int specializationId, CancellationToken cancellationToken)
    {
        if (!await Context.Specializations.AnyAsync(s => s.Id == specializationId, cancellationToken))
        {
            throw new ValidationException("specializationId", "Odabrana specijalizacija ne postoji.");
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
