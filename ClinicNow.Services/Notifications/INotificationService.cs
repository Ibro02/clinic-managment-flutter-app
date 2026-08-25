using ClinicNow.Model.Common;
using ClinicNow.Model.Dto;
using ClinicNow.Model.SearchObjects;

namespace ClinicNow.Services.Notifications;

/// <summary>
/// User-scoped notification service. Unlike codebooks/patients this has no
/// client-facing Insert/Update/Delete - notifications are only ever created
/// internally by other services (<see cref="CreateAsync"/>) in response to a
/// real domain event, per rulebook Part II §G.
/// </summary>
public interface INotificationService
{
    /// <summary>Paged list of the *current* user's own notifications, newest first.</summary>
    Task<PagedResult<NotificationDto>> GetPagedAsync(NotificationSearchObject search, CancellationToken cancellationToken = default);

    Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default);

    Task MarkAsReadAsync(int id, CancellationToken cancellationToken = default);

    Task MarkAllAsReadAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates a notification for <paramref name="userId"/> and pushes it over SignalR. Called by other services, never directly by a controller.</summary>
    Task CreateAsync(int userId, string title, string text, CancellationToken cancellationToken = default);
}
