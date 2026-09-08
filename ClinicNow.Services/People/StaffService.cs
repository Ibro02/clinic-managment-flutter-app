using ClinicNow.Model.Dto;
using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Requests;
using ClinicNow.Model.Security;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using ClinicNow.Services.Security;
using ClinicNow.Services.Validation;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;

namespace ClinicNow.Services.People;

/// <summary>
/// Staff (front-desk/administrative) accounts. Unlike Doctor/Patient, Staff has
/// no separate profile entity - the <see cref="User"/> row itself *is* the
/// resource, filtered to whoever holds the Staff role, so <c>TDbEntity</c> is
/// <see cref="User"/> directly rather than a bespoke Staff table.
///
/// Review item: the admin user list was missing Staff accounts entirely - there
/// was no endpoint, screen, or way to create one short of inserting into the
/// database directly.
/// </summary>
public class StaffService : BaseCRUDService<UserDto, StaffSearchObject, User, StaffInsertRequest, StaffUpdateRequest>, IStaffService
{
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenBlocklistService _tokenBlocklistService;

    public StaffService(
        ClinicNowContext context,
        IMapper mapper,
        IPasswordHasher passwordHasher,
        ITokenBlocklistService tokenBlocklistService) : base(context, mapper)
    {
        _passwordHasher = passwordHasher;
        _tokenBlocklistService = tokenBlocklistService;
    }

    protected override IQueryable<User> ApplyFilter(StaffSearchObject search, IQueryable<User> query)
    {
        query = query.Where(u => u.UserRoles.Any(ur => ur.Role.Name == Roles.Staff));

        if (!string.IsNullOrWhiteSpace(search.Name))
        {
            query = query.Where(u => (u.FirstName + " " + u.LastName).Contains(search.Name));
        }

        return query;
    }

    public override async Task<UserDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var user = await Context.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .SingleOrDefaultAsync(u => u.Id == id && u.UserRoles.Any(ur => ur.Role.Name == Roles.Staff), cancellationToken);

        return user is null ? null : Mapper.Map<UserDto>(user);
    }

    public override async Task<UserDto> InsertAsync(StaffInsertRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateProfile(request.FirstName, request.LastName, request.Password, request.Email, request.PhoneNumber);

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var emailTaken = await Context.Users.AnyAsync(u => u.Email == normalizedEmail, cancellationToken);
        if (emailTaken)
        {
            throw new ValidationException("email", "Korisnik sa ovom email adresom već postoji.");
        }

        var staffRole = await Context.Roles.SingleAsync(r => r.Name == Roles.Staff, cancellationToken);

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
        user.UserRoles.Add(new UserRole { Role = staffRole });

        Context.Users.Add(user);
        await Context.SaveChangesAsync(cancellationToken);

        return (await GetByIdAsync(user.Id, cancellationToken))!;
    }

    public override async Task<UserDto> UpdateAsync(int id, StaffUpdateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateProfile(request.FirstName, request.LastName, password: null, email: null, request.PhoneNumber);

        var user = await Context.Users
            .SingleOrDefaultAsync(u => u.Id == id && u.UserRoles.Any(ur => ur.Role.Name == Roles.Staff), cancellationToken)
            ?? throw new NotFoundException(nameof(User), id);

        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();

        await Context.SaveChangesAsync(cancellationToken);

        return (await GetByIdAsync(user.Id, cancellationToken))!;
    }

    /// <summary>
    /// "Delete" means deactivate, never a physical row removal: unlike
    /// Doctor/Patient (a separate profile row the base class can safely
    /// <c>Remove</c> while the linked login survives), the <see cref="User"/>
    /// row here *is* the resource, and other tables (audit logs, notifications,
    /// refunds recorded "by" this user, ...) hold real FKs to it. Overrides the
    /// base method entirely rather than the Before/After hooks, which assume
    /// the base's default remove-or-soft-delete behaviour is the right one.
    /// </summary>
    public override async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var user = await Context.Users
            .SingleOrDefaultAsync(u => u.Id == id && u.UserRoles.Any(ur => ur.Role.Name == Roles.Staff), cancellationToken)
            ?? throw new NotFoundException(nameof(User), id);

        user.IsActive = false;
        // Same cutoff DoctorService/PatientService stamp on their linked User -
        // IsActive alone only blocks a *new* login; a JWT already issued stays
        // valid until it naturally expires without this.
        user.RevokeOutstandingTokens();

        await Context.SaveChangesAsync(cancellationToken);

        _tokenBlocklistService.InvalidateTokensValidFrom(user.Id);
    }

    /// <summary>Same shared rules every account-creating flow uses (review item C17) - see DoctorService.ValidateProfile.</summary>
    private static void ValidateProfile(
        string firstName, string lastName, string? password, string? email, string? phoneNumber)
    {
        var errors = new Dictionary<string, string[]>();

        ContactRules.RequireText(errors, "firstName", firstName, ContactRules.MaxNameLength, "Ime");
        ContactRules.RequireText(errors, "lastName", lastName, ContactRules.MaxNameLength, "Prezime");
        ContactRules.OptionalPhone(errors, "phoneNumber", phoneNumber);

        if (email is not null)
        {
            ContactRules.RequireEmail(errors, "email", email);
        }

        if (password is not null)
        {
            ContactRules.RequirePassword(errors, "password", password);
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }
}
