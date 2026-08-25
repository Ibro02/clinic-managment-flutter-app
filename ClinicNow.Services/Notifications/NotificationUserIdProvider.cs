using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace ClinicNow.Services.Notifications;

/// <summary>
/// Maps a SignalR connection to <c>ClaimTypes.NameIdentifier</c> (the same claim
/// the JWT carries as the user's ID) so <c>Clients.User(userId)</c> in
/// <see cref="NotificationService"/> targets the right browser/app connection.
/// Registered in ClinicNow.API's <c>Program.cs</c> as <c>IUserIdProvider</c>.
/// </summary>
public class NotificationUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
}
