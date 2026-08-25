using ClinicNow.Model.Dto;

namespace ClinicNow.Services.Notifications;

/// <summary>
/// Strongly-typed SignalR client contract for <c>NotificationsHub</c>, shared here
/// so <see cref="NotificationService"/> (which pushes) and the Hub itself (which is
/// hosted in ClinicNow.API) agree on the exact method signature.
/// </summary>
public interface INotificationsClient
{
    Task NotificationCreated(NotificationDto notification);
}
