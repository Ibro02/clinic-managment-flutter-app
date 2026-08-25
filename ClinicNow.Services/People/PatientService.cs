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
        // MedicalRecord must be loaded for PatientDto.MedicalRecordId (rulebook
        // Part II §K: never show raw IDs / always give the UI a real link target) -
        // ApplyFilter runs on the GetPagedAsync path.
        query = query.Include(p => p.MedicalRecord);

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

    public override async Task<PatientDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        // BaseService.GetByIdAsync uses FindAsync, which doesn't support Include.
        var entity = await Context.Patients
            .Include(p => p.MedicalRecord)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        return entity is null ? null : Mapper.Map<PatientDto>(entity);
    }

    protected override async Task BeforeInsertAsync(PatientInsertRequest request, Patient entity, CancellationToken cancellationToken)
    {
        ValidatePatient(request.FirstName, request.LastName, request.Gender, request.Email);
        await EnsurePersonalIdIsUniqueAsync(request.PersonalIdNumber, excludeId: null, cancellationToken);

        entity.FirstName = request.FirstName.Trim();
        entity.LastName = request.LastName.Trim();
        entity.CreatedAtUtc = DateTime.UtcNow;
    }

    protected override async Task BeforeUpdateAsync(PatientUpdateRequest request, Patient entity, CancellationToken cancellationToken)
    {
        ValidatePatient(request.FirstName, request.LastName, request.Gender, request.Email);
        await EnsurePersonalIdIsUniqueAsync(request.PersonalIdNumber, excludeId: entity.Id, cancellationToken);
    }

    // Every patient must have exactly one medical record ("medicinski karton") -
    // created here, in the same request as the patient itself, rather than left
    // to be lazily materialized on first access, so "every patient has one
    // unique medical file" is true from the moment the patient exists, with no
    // window where a patient record could be viewed/edited without one.
    protected override async Task AfterInsertAsync(PatientInsertRequest request, Patient entity, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        Context.MedicalRecords.Add(new MedicalRecord
        {
            PatientId = entity.Id,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        await Context.SaveChangesAsync(cancellationToken);
        await Context.Entry(entity).Reference(p => p.MedicalRecord).LoadAsync(cancellationToken);
    }

    private static void ValidatePatient(string firstName, string lastName, ClinicNow.Model.Common.Gender? gender, string? email)
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

        if (gender is null)
        {
            errors["gender"] = ["Spol je obavezan."];
        }

        if (!string.IsNullOrWhiteSpace(email) && !email.Contains('@'))
        {
            errors["email"] = ["Email adresa nije ispravnog formata."];
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
