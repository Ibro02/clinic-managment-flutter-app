using ClinicNow.Model.Dto;
using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Requests;
using ClinicNow.Model.Security;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using ClinicNow.Services.Security;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;

namespace ClinicNow.Services.People;

/// <summary>
/// Doctor creation also creates the linked login <see cref="User"/> in the same
/// operation (rulebook §5: doctor accounts are never self-registered) - so this
/// overrides <see cref="InsertAsync"/>/<see cref="UpdateAsync"/> entirely instead
/// of using the generic Before/After hooks, which assume a single entity.
/// </summary>
public class DoctorService : BaseCRUDService<DoctorDto, DoctorSearchObject, Doctor, DoctorInsertRequest, DoctorUpdateRequest>
{
    private readonly IPasswordHasher _passwordHasher;

    public DoctorService(ClinicNowContext context, IMapper mapper, IPasswordHasher passwordHasher) : base(context, mapper)
    {
        _passwordHasher = passwordHasher;
    }

    protected override IQueryable<Doctor> ApplyFilter(DoctorSearchObject search, IQueryable<Doctor> query)
    {
        query = query
            .Include(d => d.User)
            .Include(d => d.Location)
            .Include(d => d.DoctorSpecializations).ThenInclude(ds => ds.Specialization);

        if (!string.IsNullOrWhiteSpace(search.Name))
        {
            query = query.Where(d => (d.User.FirstName + " " + d.User.LastName).Contains(search.Name));
        }

        if (search.SpecializationId.HasValue)
        {
            query = query.Where(d => d.DoctorSpecializations.Any(ds => ds.SpecializationId == search.SpecializationId.Value));
        }

        return query;
    }

    public override async Task<DoctorDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await Context.Doctors
            .Include(d => d.User)
            .Include(d => d.Location)
            .Include(d => d.DoctorSpecializations).ThenInclude(ds => ds.Specialization)
            .SingleOrDefaultAsync(d => d.Id == id, cancellationToken);

        return entity is null ? null : Mapper.Map<DoctorDto>(entity);
    }

    public override async Task<DoctorDto> InsertAsync(DoctorInsertRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateProfile(request.FirstName, request.LastName, request.Password);

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var emailTaken = await Context.Users.AnyAsync(u => u.Email == normalizedEmail, cancellationToken);
        if (emailTaken)
        {
            throw new ValidationException("email", "Korisnik sa ovom email adresom već postoji.");
        }

        await EnsureSpecializationsExistAsync(request.SpecializationIds, cancellationToken);
        await EnsureLocationExistsAsync(request.LocationId, cancellationToken);

        var doctorRole = await Context.Roles.SingleAsync(r => r.Name == Roles.Doctor, cancellationToken);

        var user = new User
        {
            Email = normalizedEmail,
            PasswordHash = _passwordHasher.Hash(request.Password),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim(),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        user.UserRoles.Add(new UserRole { Role = doctorRole });

        var doctor = new Doctor
        {
            User = user, // navigation, not UserId - lets EF resolve the FK in one SaveChanges (no explicit transaction needed)
            LocationId = request.LocationId,
            LicenseNumber = request.LicenseNumber,
            Bio = request.Bio,
            CreatedAtUtc = DateTime.UtcNow
        };
        foreach (var specializationId in request.SpecializationIds.Distinct())
        {
            doctor.DoctorSpecializations.Add(new DoctorSpecialization { SpecializationId = specializationId });
        }

        Context.Doctors.Add(doctor);
        await Context.SaveChangesAsync(cancellationToken);

        return (await GetByIdAsync(doctor.Id, cancellationToken))!;
    }

    public override async Task<DoctorDto> UpdateAsync(int id, DoctorUpdateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateProfile(request.FirstName, request.LastName, password: null);
        await EnsureSpecializationsExistAsync(request.SpecializationIds, cancellationToken);
        await EnsureLocationExistsAsync(request.LocationId, cancellationToken);

        var doctor = await Context.Doctors
            .Include(d => d.User)
            .Include(d => d.DoctorSpecializations)
            .SingleOrDefaultAsync(d => d.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Doctor), id);

        doctor.User.FirstName = request.FirstName.Trim();
        doctor.User.LastName = request.LastName.Trim();
        doctor.User.PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();
        doctor.LocationId = request.LocationId;
        doctor.LicenseNumber = request.LicenseNumber;
        doctor.Bio = request.Bio;

        // Replace the specialization set wholesale - simpler and just as correct
        // as diffing add/remove for a small list, and avoids stale join rows.
        Context.DoctorSpecializations.RemoveRange(doctor.DoctorSpecializations);
        foreach (var specializationId in request.SpecializationIds.Distinct())
        {
            doctor.DoctorSpecializations.Add(new DoctorSpecialization { DoctorId = doctor.Id, SpecializationId = specializationId });
        }

        await Context.SaveChangesAsync(cancellationToken);

        return (await GetByIdAsync(doctor.Id, cancellationToken))!;
    }

    protected override async Task BeforeDeleteAsync(Doctor entity, CancellationToken cancellationToken)
    {
        // Removing a Doctor profile doesn't delete the underlying login - but a
        // login with no Doctor profile behind it shouldn't stay usable, so
        // deactivate it rather than leave an orphaned active account.
        var user = await Context.Users.FindAsync([entity.UserId], cancellationToken);
        if (user is not null)
        {
            user.IsActive = false;
        }
    }

    private static void ValidateProfile(string firstName, string lastName, string? password)
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

        if (password is not null && password.Length < 8)
        {
            errors["password"] = ["Lozinka mora imati najmanje 8 karaktera."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    private async Task EnsureSpecializationsExistAsync(List<int> specializationIds, CancellationToken cancellationToken)
    {
        if (specializationIds.Count == 0)
        {
            return;
        }

        var distinctIds = specializationIds.Distinct().ToList();
        var existingCount = await Context.Specializations.CountAsync(s => distinctIds.Contains(s.Id), cancellationToken);

        if (existingCount != distinctIds.Count)
        {
            throw new ValidationException("specializationIds", "Jedna ili više odabranih specijalizacija ne postoji.");
        }
    }

    private async Task EnsureLocationExistsAsync(int locationId, CancellationToken cancellationToken)
    {
        var exists = await Context.Locations.AnyAsync(l => l.Id == locationId, cancellationToken);
        if (!exists)
        {
            throw new ValidationException("locationId", "Odabrana lokacija ne postoji.");
        }
    }
}
