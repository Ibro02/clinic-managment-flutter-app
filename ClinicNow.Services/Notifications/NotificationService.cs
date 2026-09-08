using System.Security.Claims;
using ClinicNow.Model.Common;
using ClinicNow.Model.Dto;
using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Messaging;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Services.Messaging;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using MapsterMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicNow.Services.Notifications;

public class NotificationService : INotificationService
{
    private readonly ClinicNowContext _context;
    private readonly IMapper _mapper;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IPushPublisher _pushPublisher;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        ClinicNowContext context,
        IMapper mapper,
        IHttpContextAccessor httpContextAccessor,
        IPushPublisher pushPublisher,
        ILogger<NotificationService> logger)
    {
        _context = context;
        _mapper = mapper;
        _httpContextAccessor = httpContextAccessor;
        _pushPublisher = pushPublisher;
        _logger = logger;
    }

    public async Task<PagedResult<NotificationDto>> GetPagedAsync(NotificationSearchObject search, CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserId();

        var query = _context.Notifications.Where(n => n.UserId == userId);

        if (search.IsRead.HasValue)
        {
            query = query.Where(n => n.IsRead == search.IsRead.Value);
        }

        query = query.OrderByDescending(n => n.CreatedAtUtc);

        var count = await query.CountAsync(cancellationToken);

        // AsNoTracking: a read path, and the busiest one in the system - both
        // clients poll this list, so it pays for a change-tracker snapshot per
        // notification more often than any other query. This service overrides
        // GetPagedAsync rather than inheriting BaseService's, so it does not pick
        // up that class's no-tracking read automatically.
        var entities = await query
            .AsNoTracking()
            .Skip((search.Page - 1) * search.PageSize)
            .Take(search.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<NotificationDto>
        {
            Count = count,
            ResultList = _mapper.Map<List<NotificationDto>>(entities)
        };
    }

    public Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserId();
        return _context.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, cancellationToken);
    }

    public async Task MarkAsReadAsync(int id, CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserId();

        var notification = await _context.Notifications.SingleOrDefaultAsync(n => n.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Notification), id);

        if (notification.UserId != userId)
        {
            throw new ForbiddenException("Ne možete označiti tuđu obavijest kao pročitanu.");
        }

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task MarkAllAsReadAsync(CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserId();

        var unread = await _context.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync(cancellationToken);

        if (unread.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var notification in unread)
        {
            notification.IsRead = true;
            notification.ReadAtUtc = now;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task CreateAsync(int userId, string title, string text, CancellationToken cancellationToken = default)
    {
        var notification = new Notification
        {
            UserId = userId,
            Title = title,
            Text = text,
            IsRead = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync(cancellationToken);

        // No real-time hub push: both Flutter clients get their auto-refresh
        // from NotificationCenter's own polling of this same list (review item
        // 15 explicitly permits "SignalR ili polling" - a hub with no
        // connecting client was dead code, not a working feature, so it was
        // removed rather than left unused).
        await PublishDevicePushAsync(notification, userId);
    }

    /// <summary>
    /// Queues the same notification as a device push, so it reaches the user
    /// when the app is closed - the one thing in-app polling cannot do, since
    /// that needs the app to be running.
    ///
    /// Best-effort: the notification is already committed by this point, and a
    /// broker outage must not turn a completed booking into an error. No token
    /// means no push and nothing to log loudly about - most staff accounts
    /// will never register one.
    /// </summary>
    private async Task PublishDevicePushAsync(Notification notification, int userId)
    {
        try
        {
            var tokens = await _context.DeviceTokens
                .Where(t => t.UserId == userId)
                .Select(t => t.Token)
                .ToListAsync();

            if (tokens.Count == 0)
            {
                return;
            }

            await _pushPublisher.PublishAsync(
                new PushMessage
                {
                    Tokens = tokens,
                    Title = notification.Title,
                    Body = notification.Text,
                    NotificationId = notification.Id
                },
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Queueing a device push for notification {NotificationId} to user {UserId} failed; the notification itself is persisted.",
                notification.Id, userId);
        }
    }

    private int CurrentUserId()
    {
        var idClaim = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new AuthenticationException("Korisnik nije autentifikovan.");
        return int.Parse(idClaim);
    }
}
