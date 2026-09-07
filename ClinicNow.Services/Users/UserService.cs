using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using ClinicNow.Model.Dto;
using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Messaging;
using ClinicNow.Model.Requests;
using ClinicNow.Model.Security;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using ClinicNow.Services.Messaging;
using ClinicNow.Services.Security;
using ClinicNow.Services.Validation;
using MapsterMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace ClinicNow.Services.Users;

/// <summary>
/// Scoped (holds a <see cref="ClinicNowContext"/>) - never Transient/Singleton, per
/// rulebook Part II §D. Uses <see cref="IHttpContextAccessor"/> to read the current
/// user's claims rather than any controller parsing a token manually (rulebook §D).
/// </summary>
public class UserService : IUserService
{
    private readonly ClinicNowContext _context;
    private readonly IMapper _mapper;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly ITokenBlocklistService _tokenBlocklistService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IEmailPublisher _emailPublisher;

    /// <summary>Short enough that a leaked email is rarely still redeemable, long enough to walk to the inbox.</summary>
    private static readonly TimeSpan ResetCodeLifetime = TimeSpan.FromMinutes(30);

    private const int ResetCodeLength = 12;

    public UserService(
        ClinicNowContext context,
        IMapper mapper,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        ITokenBlocklistService tokenBlocklistService,
        IHttpContextAccessor httpContextAccessor,
        IEmailPublisher emailPublisher)
    {
        _context = context;
        _mapper = mapper;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _tokenBlocklistService = tokenBlocklistService;
        _httpContextAccessor = httpContextAccessor;
        _emailPublisher = emailPublisher;
    }

    public async Task<UserDto> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRegistration(request);

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var emailTaken = await _context.Users.AnyAsync(u => u.Email == normalizedEmail, cancellationToken);
        if (emailTaken)
        {
            throw new ValidationException("email", "Korisnik sa ovom email adresom već postoji.");
        }

        var patientRole = await _context.Roles.SingleAsync(r => r.Name == Roles.Patient, cancellationToken);

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
        user.UserRoles.Add(new UserRole { Role = patientRole });

        // A self-registered patient needs a real Patient medical record too, not
        // just a login (Phase 3) - created here, in the same operation, via
        // navigation (User = user) so EF resolves both FKs in one SaveChanges.
        // Medical-record-only fields (PersonalIdNumber/DateOfBirth/Address)
        // aren't known at registration time; staff fill those in on first visit.
        var patient = new Patient
        {
            User = user,
            FirstName = user.FirstName,
            LastName = user.LastName,
            PhoneNumber = user.PhoneNumber,
            Email = user.Email,
            CreatedAtUtc = user.CreatedAtUtc
        };

        // "Every patient has exactly one medical file" must hold from the
        // moment the patient exists, including a self-registered one - not
        // just patients created through PatientService (whose AfterInsertAsync
        // hook does the same thing for the staff-facing walk-in flow). Created
        // in the same SaveChanges via navigation, same reasoning as User/Patient above.
        patient.MedicalRecord = new MedicalRecord
        {
            CreatedAtUtc = user.CreatedAtUtc,
            UpdatedAtUtc = user.CreatedAtUtc
        };

        _context.Patients.Add(patient);
        await _context.SaveChangesAsync(cancellationToken);

        return _mapper.Map<UserDto>(user);
    }

    public async Task<LoginResponseDto> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedEmail = (request.Email ?? string.Empty).Trim().ToLowerInvariant();

        var user = await _context.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .SingleOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        // Deliberately the same generic message for "no such user" and "wrong
        // password" - a specific message would let a caller enumerate valid emails.
        const string invalidCredentialsMessage = "Pogrešan email ili lozinka.";

        if (user is null || !_passwordHasher.Verify(request.Password ?? string.Empty, user.PasswordHash))
        {
            throw new AuthenticationException(invalidCredentialsMessage);
        }

        if (!user.IsActive)
        {
            throw new AuthenticationException("Nalog je deaktiviran. Obratite se administratoru.");
        }

        var roleNames = user.UserRoles.Select(ur => ur.Role.Name).ToList();
        var (accessToken, expiresAtUtc) = _tokenService.CreateAccessToken(user, roleNames);

        return new LoginResponseDto
        {
            AccessToken = accessToken,
            ExpiresAtUtc = expiresAtUtc,
            User = _mapper.Map<UserDto>(user)
        };
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var principal = _httpContextAccessor.HttpContext?.User
            ?? throw new AuthenticationException("Nema aktivne sesije.");

        var jti = FindClaimValue(principal, JwtRegisteredClaimNames.Jti);
        var expClaim = FindClaimValue(principal, JwtRegisteredClaimNames.Exp);

        if (string.IsNullOrEmpty(jti) || expClaim is null || !long.TryParse(expClaim, out var expUnixSeconds))
        {
            throw new AuthenticationException("Token ne sadrži očekivane podatke za odjavu.");
        }

        var expiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(expUnixSeconds).UtcDateTime;
        await _tokenBlocklistService.RevokeAsync(jti, expiresAtUtc, cancellationToken);
    }

    public async Task<UserDto> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();

        var user = await _context.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .SingleOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException(nameof(User), userId);

        return _mapper.Map<UserDto>(user);
    }

    public async Task<UserDto> UpdateCurrentUserAsync(UpdateProfileRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new Dictionary<string, string[]>();
        ContactRules.RequireText(errors, "firstName", request.FirstName, ContactRules.MaxNameLength, "Ime");
        ContactRules.RequireText(errors, "lastName", request.LastName, ContactRules.MaxNameLength, "Prezime");
        ContactRules.OptionalPhone(errors, "phoneNumber", request.PhoneNumber);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        // The id comes from the token, never the request (rulebook §5/Part II §F).
        var userId = GetCurrentUserId();
        var user = await _context.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .SingleOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException(nameof(User), userId);

        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();
        user.EmailRemindersEnabled = request.EmailRemindersEnabled;

        // The patient's medical record carries its own copy of these fields
        // (they exist for walk-in patients with no account at all), so a
        // profile edit that skipped it would leave staff looking at the old
        // name on the chart while the patient sees the new one in the app.
        // IgnoreQueryFilters: an archived patient still gets kept consistent
        // rather than silently diverging.
        var patient = await _context.Patients
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (patient is not null)
        {
            patient.FirstName = user.FirstName;
            patient.LastName = user.LastName;
            patient.PhoneNumber = user.PhoneNumber;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return _mapper.Map<UserDto>(user);
    }

    public async Task ChangeCurrentUserPasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrEmpty(request.CurrentPassword))
        {
            errors["currentPassword"] = ["Unesite trenutnu lozinku."];
        }
        ContactRules.RequirePassword(errors, "newPassword", request.NewPassword);
        if (request.NewPassword != request.ConfirmNewPassword)
        {
            errors["confirmNewPassword"] = ["Potvrda lozinke se ne poklapa sa novom lozinkom."];
        }
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var userId = GetCurrentUserId();
        var user = await _context.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException(nameof(User), userId);

        // Proving knowledge of the current password is what stops an unattended
        // signed-in phone from becoming a permanent account takeover.
        if (!_passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            throw new ValidationException("currentPassword", "Trenutna lozinka nije tačna.");
        }

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        // Any reset code still outstanding is now meaningless, and leaving it
        // valid would be a second, weaker way into an account the owner just
        // secured.
        ClearResetToken(user);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task RequestPasswordResetAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedEmail = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
        var user = await _context.Users.SingleOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        // No such account, or a deactivated one: return quietly. Saying
        // "nepoznat email" here would turn this endpoint into a way to test
        // which addresses are registered at the clinic - which for a medical
        // system is itself a disclosure.
        if (user is null || !user.IsActive)
        {
            return;
        }

        var code = GenerateResetCode();
        user.PasswordResetTokenHash = _passwordHasher.Hash(code);
        user.PasswordResetTokenExpiresAtUtc = DateTime.UtcNow.Add(ResetCodeLifetime);
        await _context.SaveChangesAsync(cancellationToken);

        // Off the request path via RabbitMQ, like every other email in the
        // system (CLAUDE.md §9). A broker outage means the code is unusable
        // rather than wrong - the user simply asks for another one, and the
        // failure is logged by the publisher.
        await _emailPublisher.PublishAsync(new EmailMessage
        {
            To = user.Email,
            Subject = "ClinicNow - kod za resetovanje lozinke",
            Body = $"Vaš kod za resetovanje lozinke je: {code}\n\n"
                + $"Kod vrijedi {ResetCodeLifetime.TotalMinutes:0} minuta i može se iskoristiti samo jednom.\n"
                + "Ako niste tražili resetovanje lozinke, zanemarite ovu poruku - vaša lozinka nije promijenjena."
        }, cancellationToken);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            errors["code"] = ["Unesite kod koji ste dobili na email."];
        }
        ContactRules.RequirePassword(errors, "newPassword", request.NewPassword);
        if (request.NewPassword != request.ConfirmNewPassword)
        {
            errors["confirmNewPassword"] = ["Potvrda lozinke se ne poklapa sa novom lozinkom."];
        }
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var normalizedEmail = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
        var user = await _context.Users.SingleOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        // One message for every way this can fail - unknown email, no reset
        // pending, wrong code, expired code. Distinguishing them would tell an
        // attacker which half of the pair they got right.
        const string invalidCodeMessage = "Kod nije ispravan ili je istekao. Zatražite novi kod.";

        if (user is null
            || user.PasswordResetTokenHash is null
            || user.PasswordResetTokenExpiresAtUtc is not DateTime expiresAtUtc
            || expiresAtUtc <= DateTime.UtcNow
            || !_passwordHasher.Verify(NormalizeResetCode(request.Code), user.PasswordResetTokenHash))
        {
            throw new ValidationException("code", invalidCodeMessage);
        }

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        // Single-use: clearing the hash here is what stops the same email being
        // replayed to take the account again later.
        ClearResetToken(user);
        await _context.SaveChangesAsync(cancellationToken);
    }

    private static void ClearResetToken(User user)
    {
        user.PasswordResetTokenHash = null;
        user.PasswordResetTokenExpiresAtUtc = null;
    }

    /// <summary>
    /// A 12-character code from a cryptographic RNG (rulebook Part II §F:
    /// <c>RandomNumberGenerator</c>, never <c>System.Random</c>), grouped for
    /// typing. The alphabet drops the characters people confuse on a phone
    /// screen (0/O, 1/I/L), which is why it is 32 symbols: 32^12 is about 2^60
    /// possibilities behind a 30-minute expiry and a rate-limited endpoint.
    /// </summary>
    private static string GenerateResetCode()
    {
        const string alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789#";
        var characters = new char[ResetCodeLength];
        // GetInt32 is rejection-sampled internally, so no modulo bias even
        // though the alphabet length does not divide the RNG's range evenly.
        for (var i = 0; i < characters.Length; i++)
        {
            characters[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        }

        var code = new string(characters);
        return $"{code[..4]}-{code[4..8]}-{code[8..]}";
    }

    /// <summary>
    /// Accepts the code however the user retyped it - with or without the
    /// dashes, in either case - since the grouping is a readability aid, not
    /// part of the secret.
    /// </summary>
    private static string NormalizeResetCode(string code)
    {
        var stripped = new string(code.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return stripped.Length == ResetCodeLength
            ? $"{stripped[..4]}-{stripped[4..8]}-{stripped[8..]}"
            : code.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Resolves the acting user's ID strictly from the validated JWT - never from a
    /// route parameter or request body (rulebook §5). Shared by every future service
    /// that needs to enforce "users can only touch their own data".
    /// </summary>
    private int GetCurrentUserId()
    {
        var principal = _httpContextAccessor.HttpContext?.User
            ?? throw new AuthenticationException("Nema aktivne sesije.");

        var idClaim = FindClaimValue(principal, ClaimTypes.NameIdentifier)
            ?? throw new AuthenticationException("Token ne sadrži identifikator korisnika.");

        return int.Parse(idClaim);
    }

    /// <summary>
    /// Equivalent to ASP.NET Core's <c>ClaimsPrincipal.FindFirstValue</c> extension
    /// (not referenced directly to avoid pulling in the full ASP.NET Core hosting
    /// package into this non-web class library).
    /// </summary>
    private static string? FindClaimValue(ClaimsPrincipal principal, string claimType) =>
        principal.Claims.FirstOrDefault(c => c.Type == claimType)?.Value;

    private static void ValidateRegistration(RegisterRequest request)
    {
        // These rules used to live here as private regexes - the strongest of the
        // three flows that fill these columns. They now live in ContactRules so
        // DoctorService and PatientService enforce the same thing (review item C17).
        var errors = new Dictionary<string, string[]>();

        ContactRules.RequireEmail(errors, "email", request.Email);
        ContactRules.RequirePassword(errors, "password", request.Password);
        ContactRules.RequireText(errors, "firstName", request.FirstName, ContactRules.MaxNameLength, "Ime");
        ContactRules.RequireText(errors, "lastName", request.LastName, ContactRules.MaxNameLength, "Prezime");
        ContactRules.OptionalPhone(errors, "phoneNumber", request.PhoneNumber);

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }
}
