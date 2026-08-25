using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.RegularExpressions;
using ClinicNow.Model.Dto;
using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Requests;
using ClinicNow.Model.Security;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using ClinicNow.Services.Security;
using MapsterMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace ClinicNow.Services.Users;

/// <summary>
/// Scoped (holds a <see cref="ClinicNowContext"/>) - never Transient/Singleton, per
/// rulebook Part II §D. Uses <see cref="IHttpContextAccessor"/> to read the current
/// user's claims rather than any controller parsing a token manually (rulebook §D).
/// </summary>
public partial class UserService : IUserService
{
    private const int MinPasswordLength = 8;

    private readonly ClinicNowContext _context;
    private readonly IMapper _mapper;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly ITokenBlocklistService _tokenBlocklistService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public UserService(
        ClinicNowContext context,
        IMapper mapper,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        ITokenBlocklistService tokenBlocklistService,
        IHttpContextAccessor httpContextAccessor)
    {
        _context = context;
        _mapper = mapper;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _tokenBlocklistService = tokenBlocklistService;
        _httpContextAccessor = httpContextAccessor;
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
            CreatedAtUtc = user.CreatedAtUtc
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
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Email) || !EmailPattern().IsMatch(request.Email))
        {
            errors["email"] = ["Unesite ispravnu email adresu (npr. ime@primjer.com)."];
        }

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < MinPasswordLength)
        {
            errors["password"] = [$"Lozinka mora imati najmanje {MinPasswordLength} karaktera."];
        }

        if (string.IsNullOrWhiteSpace(request.FirstName))
        {
            errors["firstName"] = ["Ime je obavezno."];
        }

        if (string.IsNullOrWhiteSpace(request.LastName))
        {
            errors["lastName"] = ["Prezime je obavezno."];
        }

        if (!string.IsNullOrWhiteSpace(request.PhoneNumber) && !PhonePattern().IsMatch(request.PhoneNumber))
        {
            errors["phoneNumber"] = ["Unesite ispravan broj telefona (npr. +38761123456)."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"^\+?[0-9 ]{6,20}$")]
    private static partial Regex PhonePattern();
}
