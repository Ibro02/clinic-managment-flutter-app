using System.Security.Claims;
using ClinicNow.Model.Common;
using ClinicNow.Model.Dto;
using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using MapsterMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace ClinicNow.Services.People;

public class PatientService : BaseCRUDService<PatientDto, PatientSearchObject, Patient, PatientInsertRequest, PatientUpdateRequest>, IPatientService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PatientService(ClinicNowContext context, IMapper mapper, IHttpContextAccessor httpContextAccessor)
        : base(context, mapper)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public async Task<PatientDto> GetOwnAsync(CancellationToken cancellationToken = default)
    {
        // Identity comes from the token via IHttpContextAccessor, never from a
        // route or body value (rulebook Part II §D/§F) - the same shape every
        // other "my own record" lookup in this codebase uses.
        var principal = _httpContextAccessor.HttpContext?.User
            ?? throw new AuthenticationException("Nema aktivne sesije.");
        var userId = int.Parse(principal.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        // MedicalRecord has to be Include()-d for PatientDto.MedicalRecordId to
        // populate, exactly as ApplyFilter/GetByIdAsync do it.
        var patient = await Context.Patients
            .Include(p => p.MedicalRecord)
            .SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("Nije pronađen medicinski karton za ovaj nalog.");

        return Mapper.Map<PatientDto>(patient);
    }

    protected override IQueryable<Patient> ApplyFilter(PatientSearchObject search, IQueryable<Patient> query)
    {
        // MedicalRecord must be loaded for PatientDto.MedicalRecordId (rulebook
        // Part II §K: never show raw IDs / always give the UI a real link target) -
        // ApplyFilter runs on the GetPagedAsync path.
        query = query.Include(p => p.MedicalRecord);

        // The dedicated "Arhivirani pacijenti" screen deliberately bypasses the
        // global ISoftDelete query filter instead of ever mixing archived rows
        // into the normal list - Administrator/Staff asked to see archived
        // patients as their own view, not as noise in the everyday one.
        query = search.OnlyDeleted
            ? query.IgnoreQueryFilters().Where(p => p.IsDeleted)
            : query;

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

    protected override async Task BeforeDeleteAsync(Patient entity, CancellationToken cancellationToken)
    {
        // Deleting a Patient is a soft-delete (ISoftDelete), i.e. archiving, not a
        // physical removal - so unlike a hard-delete guard, this only needs to
        // protect a *future* appointment from being silently orphaned. A patient
        // whose appointment history is entirely Completed/Cancelled can be
        // archived; AppointmentMappingConfig/MedicalRecordMappingConfig/
        // MedicalDocumentMappingConfig keep that history readable afterwards
        // (review item C3) instead of blocking archival altogether.
        var hasActiveAppointment = await Context.Appointments.AnyAsync(
            a => a.PatientId == entity.Id &&
                (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed),
            cancellationToken);
        if (hasActiveAppointment)
        {
            throw new BusinessException(
                $"Pacijent '{entity.FirstName} {entity.LastName}' se ne može obrisati jer ima zakazan ili potvrđen termin.");
        }

        // Mirrors DoctorService.BeforeDeleteAsync: a login with no active patient
        // profile behind it shouldn't stay usable. Patient.UserId is nullable (a
        // walk-in patient staff created may have no login at all), unlike
        // Doctor.UserId, so this is null-checked rather than assumed present -
        // this is the fix for the reviewer's actual defect: deleting a patient
        // used to leave User.IsActive untouched, so the login still worked via
        // UserService.LoginAsync after the patient was "deleted".
        if (entity.UserId is int userId)
        {
            var user = await Context.Users.FindAsync([userId], cancellationToken);
            if (user is not null)
            {
                user.IsActive = false;
            }
        }
    }

    public async Task<PatientDto> RestoreAsync(int id, CancellationToken cancellationToken = default)
    {
        // The global ISoftDelete query filter excludes archived rows by default,
        // so a plain FindAsync/Where here would never find the patient we're
        // trying to restore - IgnoreQueryFilters() is required.
        var entity = await Context.Patients
            .IgnoreQueryFilters()
            .Include(p => p.MedicalRecord)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Patient), id);

        if (!entity.IsDeleted)
        {
            throw new BusinessException($"Pacijent '{entity.FirstName} {entity.LastName}' nije arhiviran.");
        }

        entity.IsDeleted = false;
        entity.DeletedAtUtc = null;

        // The exact reverse of BeforeDeleteAsync's deactivation - restoring a
        // patient's record should restore their ability to log in too.
        if (entity.UserId is int userId)
        {
            var user = await Context.Users.FindAsync([userId], cancellationToken);
            if (user is not null)
            {
                user.IsActive = true;
            }
        }

        await Context.SaveChangesAsync(cancellationToken);

        return Mapper.Map<PatientDto>(entity);
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
