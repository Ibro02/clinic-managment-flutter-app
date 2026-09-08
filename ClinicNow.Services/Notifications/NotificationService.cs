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
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicNow.Services.Notifications;

public class NotificationService : INotificationService
{
    private readonly ClinicNowContext _context;
    private readonly IMapper _mapper;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IHubContext<NotificationsHub, INotificationsClient> _hubContext;
    private readonly IPushPublisher _pushPublisher;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        ClinicNowContext context,
        IMapper mapper,
        IHttpContextAccessor httpContextAccessor,
        IHubContext<NotificationsHub, INotificationsClient> hubContext,
        IPushPublisher pushPublisher,
        ILogger<NotificationService> logger)
    {
        _context = context;
        _mapper = mapper;
        _httpContextAccessor = httpContextAccessor;
        _hubContext = hubContext;
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

        var dto = _mapper.Map<NotificationDto>(notification);

        // Best-effort real-time push, and now actually best-effort (review item
        // C15). The notification is already committed by this point, so a hub
        // that is unreachable, a transport that just dropped, or a caller whose
        // request was cancelled mid-flight must not turn a succeeded operation
        // - a booking, a cancellation, a captured payment - into an error the
        // caller sees. Clients re-read the list on their own poll regardless,
        // so the cost of a lost push is latency, never a lost notification.
        //
        // Deliberately no CancellationToken: cancelling the *caller's* request
        // is not a reason to skip informing the user about work that already
        // committed.
        try
        {
            await _hubContext.Clients.User(userId.ToString()).NotificationCreated(dto);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Real-time push of notification {NotificationId} to user {UserId} failed; it is persisted and will appear on the client's next refresh.",
                notification.Id, userId);
        }

        await PublishDevicePushAsync(notification, userId);
    }

    /// <summary>
    /// Queues the same notification as a device push, so it reaches the user
    /// when the app is closed - the one thing SignalR and polling cannot do,
    /// since both need the app to be running.
    ///
    /// Best-effort for the same reason the hub push above is: the notification
    /// is already committed, and a broker outage must not turn a completed
    /// booking into an error. No token means no push and nothing to log loudly
    /// about - most staff accounts will never register one.
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
