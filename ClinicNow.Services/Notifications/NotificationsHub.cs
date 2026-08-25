using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ClinicNow.Services.Notifications;

/// <summary>
/// Real-time push for in-app notifications (rulebook Part II §G: auto-refresh via
/// SignalR/polling, no manual refresh). Kept in ClinicNow.Services (not the API
/// project) purely so <see cref="NotificationService"/> can inject
/// <see cref="IHubContext{THub,TClient}"/> without a circular project reference;
/// it's still mapped from ClinicNow.API's <c>Program.cs</c>
/// (<c>app.MapHub&lt;NotificationsHub&gt;(...)</c>).
///
/// Every connection must be authenticated (JWT, delivered via the
/// <c>access_token</c> query string since browsers/WebSockets can't set an
/// Authorization header - see <c>Program.cs</c>'s <c>OnMessageReceived</c>). Clients
/// don't call any hub method themselves; the server only ever pushes.
/// </summary>
[Authorize]
public class NotificationsHub : Hub<INotificationsClient>
{
}
