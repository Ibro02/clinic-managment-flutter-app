using ClinicNow.Model.Dto;
using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;

namespace ClinicNow.Services.People;

public class PatientService : BaseCRUDService<PatientDto, PatientSearchObject, Patient, PatientInsertRequest, PatientUpdateRequest>
{
    public PatientService(ClinicNowContext context, IMapper mapper) : base(context, mapper)
    {
    }

    protected override IQueryable<Patient> ApplyFilter(PatientSearchObject search, IQueryable<Patient> query)
    {
        if (!string.IsNullOrWhiteSpace(search.Name))
        {
            query = query.Where(p => (p.FirstName + " " + p.LastName).Contains(search.Name));
        }

        if (!string.IsNullOrWhiteSpace(search.PersonalIdNumber))
        {
            query = query.Where(p => p.PersonalIdNumber != null && p.PersonalIdNumber.Contains(search.PersonalIdNumber));
        }

        return query;
    }

    protected override async Task BeforeInsertAsync(PatientInsertRequest request, Patient entity, CancellationToken cancellationToken)
    {
        ValidateNames(request.FirstName, request.LastName);
        await EnsurePersonalIdIsUniqueAsync(request.PersonalIdNumber, excludeId: null, cancellationToken);

        entity.FirstName = request.FirstName.Trim();
        entity.LastName = request.LastName.Trim();
        entity.CreatedAtUtc = DateTime.UtcNow;
    }

    protected override async Task BeforeUpdateAsync(PatientUpdateRequest request, Patient entity, CancellationToken cancellationToken)
    {
        ValidateNames(request.FirstName, request.LastName);
        await EnsurePersonalIdIsUniqueAsync(request.PersonalIdNumber, excludeId: entity.Id, cancellationToken);
    }

    private static void ValidateNames(string firstName, string lastName)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(firstName))
        {
            errors["firstName"] = ["Ime je obavezno."];
        }

        if (string.IsNullOrWhiteSpace(lastName))
        {
            errors["lastName"] = ["Prezime je obavezno."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    private async Task EnsurePersonalIdIsUniqueAsync(string? personalIdNumber, int? excludeId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(personalIdNumber))
        {
            return;
        }

        var exists = await Context.Patients.AnyAsync(
            p => p.PersonalIdNumber == personalIdNumber && p.Id != (excludeId ?? 0), cancellationToken);

        if (exists)
        {
            throw new ValidationException("personalIdNumber", "Pacijent sa ovim JMBG/ID brojem već postoji.");
        }
    }
}
