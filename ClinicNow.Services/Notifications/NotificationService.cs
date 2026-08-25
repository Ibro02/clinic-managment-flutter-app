using System.Security.Claims;
using ClinicNow.Model.Common;
using ClinicNow.Model.Dto;
using ClinicNow.Model.Exceptions;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using MapsterMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ClinicNow.Services.Notifications;

public class NotificationService : INotificationService
{
    private readonly ClinicNowContext _context;
    private readonly IMapper _mapper;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IHubContext<NotificationsHub, INotificationsClient> _hubContext;

    public NotificationService(
        ClinicNowContext context,
        IMapper mapper,
        IHttpContextAccessor httpContextAccessor,
        IHubContext<NotificationsHub, INotificationsClient> hubContext)
    {
        _context = context;
        _mapper = mapper;
        _httpContextAccessor = httpContextAccessor;
        _hubContext = hubContext;
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
        var entities = await query
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
        // Best-effort real-time push - a missed push still leaves the
        // notification correctly persisted for the client's next poll/list.
        await _hubContext.Clients.User(userId.ToString()).NotificationCreated(dto);
    }

    private int CurrentUserId()
    {
        var idClaim = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new AuthenticationException("Korisnik nije autentifikovan.");
        return int.Parse(idClaim);
    }
}
