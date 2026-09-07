using System.Security.Claims;
using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Requests;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicNow.Services.Notifications;

/// <inheritdoc cref="IDeviceTokenService"/>
public class DeviceTokenService : IDeviceTokenService
{
    private const int MaxTokenLength = 512;
    private const int MaxPlatformLength = 20;

    private readonly ClinicNowContext _context;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<DeviceTokenService> _logger;

    public DeviceTokenService(
        ClinicNowContext context,
        IHttpContextAccessor httpContextAccessor,
        ILogger<DeviceTokenService> logger)
    {
        _context = context;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task RegisterAsync(RegisterDeviceTokenRequest request, CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserId();
        var token = (request.Token ?? string.Empty).Trim();
        var platform = (request.Platform ?? string.Empty).Trim();

        Validate(token, platform);

        var now = DateTime.UtcNow;
        var existing = await _context.DeviceTokens.SingleOrDefaultAsync(t => t.Token == token, cancellationToken);

        if (existing is null)
        {
            _context.DeviceTokens.Add(new DeviceToken
            {
                UserId = userId,
                Token = token,
                Platform = platform,
                CreatedAtUtc = now,
                LastSeenAtUtc = now
            });
        }
        else
        {
            // Reassigning rather than rejecting is the point: a shared or
            // handed-on device keeps its FCM token when a different person signs
            // in, and leaving it pointed at the previous account would deliver
            // one user's medical notifications to another.
            if (existing.UserId != userId)
            {
                _logger.LogInformation(
                    "Device token reassigned from user {PreviousUserId} to user {UserId}.", existing.UserId, userId);
                existing.UserId = userId;
            }

            existing.Platform = platform;
            existing.LastSeenAtUtc = now;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UnregisterAsync(UnregisterDeviceTokenRequest request, CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserId();
        var token = (request.Token ?? string.Empty).Trim();

        if (token.Length == 0)
        {
            return;
        }

        // Scoped to the caller's own tokens: without the UserId predicate this
        // endpoint would let any authenticated user silence any other user's
        // device by guessing or replaying a token.
        var existing = await _context.DeviceTokens
            .SingleOrDefaultAsync(t => t.Token == token && t.UserId == userId, cancellationToken);

        if (existing is null)
        {
            return;
        }

        _context.DeviceTokens.Remove(existing);
        await _context.SaveChangesAsync(cancellationToken);
    }

    private static void Validate(string token, string platform)
    {
        var errors = new Dictionary<string, string[]>();

        if (token.Length == 0)
        {
            errors["token"] = ["Token uređaja je obavezan."];
        }
        else if (token.Length > MaxTokenLength)
        {
            errors["token"] = [$"Token uređaja može imati najviše {MaxTokenLength} karaktera."];
        }

        if (platform.Length == 0)
        {
            errors["platform"] = ["Platforma je obavezna."];
        }
        else if (platform.Length > MaxPlatformLength)
        {
            errors["platform"] = [$"Platforma može imati najviše {MaxPlatformLength} karaktera."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    private int CurrentUserId()
    {
        var idClaim = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new AuthenticationException("Korisnik nije autentifikovan.");
        return int.Parse(idClaim);
    }
}
