using ClinicNow.Model.Dto;
using ClinicNow.Services.Database;
using ClinicNow.Services.Notifications;
using ClinicNow.Tests.TestSupport;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClinicNow.Tests.Notifications;

/// <summary>
/// Review item C15's backend half: the real-time push is best-effort, so a hub
/// that cannot deliver must never turn an operation that already committed -
/// a booking, a cancellation, a captured payment - into an error for the caller.
/// </summary>
public class NotificationServiceTests
{
    private const int SeededUserId = 1;

    private static NotificationService CreateService(ClinicNowContext context, IHubContext<NotificationsHub, INotificationsClient> hub) =>
        new(
            context,
            TestContextFactory.CreateMapper(),
            TestContextFactory.CreateHttpContextAccessor(SeededUserId),
            hub,
            NullLogger<NotificationService>.Instance);

    [Fact]
    public async Task CreateAsync_persists_the_notification_and_pushes_it()
    {
        using var context = TestContextFactory.CreateContext();
        var hub = new RecordingHubContext();
        var service = CreateService(context, hub);

        await service.CreateAsync(SeededUserId, "Termin potvrđen", "Vaš termin je potvrđen.");

        var saved = await context.Notifications.SingleAsync(n => n.Title == "Termin potvrđen");
        Assert.Equal(SeededUserId, saved.UserId);
        Assert.False(saved.IsRead);

        Assert.Equal(SeededUserId.ToString(), hub.PushedToUserId);
        Assert.Equal("Termin potvrđen", hub.Pushed?.Title);
    }

    [Fact]
    public async Task CreateAsync_still_persists_when_the_realtime_push_fails()
    {
        using var context = TestContextFactory.CreateContext();
        var service = CreateService(context, new ThrowingHubContext());

        // The whole point: no exception escapes to the caller, whose own work
        // (the booking that triggered this) is already committed.
        await service.CreateAsync(SeededUserId, "Termin otkazan", "Vaš termin je otkazan.");

        var saved = await context.Notifications.SingleAsync(n => n.Title == "Termin otkazan");
        Assert.Equal(SeededUserId, saved.UserId);
        Assert.False(saved.IsRead);
    }

    private sealed class RecordingHubContext : IHubContext<NotificationsHub, INotificationsClient>
    {
        public NotificationDto? Pushed { get; private set; }

        public string? PushedToUserId { get; private set; }

        public IHubClients<INotificationsClient> Clients => new Proxies(this);

        public IGroupManager Groups => throw new NotSupportedException();

        private sealed class Proxies : HubClientsBase, IHubClients<INotificationsClient>
        {
            private readonly RecordingHubContext _owner;

            public Proxies(RecordingHubContext owner) => _owner = owner;

            public override INotificationsClient User(string userId) => new Client(_owner, userId);
        }

        private sealed class Client : INotificationsClient
        {
            private readonly RecordingHubContext _owner;
            private readonly string _userId;

            public Client(RecordingHubContext owner, string userId)
            {
                _owner = owner;
                _userId = userId;
            }

            public Task NotificationCreated(NotificationDto notification)
            {
                _owner.Pushed = notification;
                _owner.PushedToUserId = _userId;
                return Task.CompletedTask;
            }
        }
    }

    /// <summary>Stands in for a hub whose transport is down mid-push.</summary>
    private sealed class ThrowingHubContext : IHubContext<NotificationsHub, INotificationsClient>
    {
        public IHubClients<INotificationsClient> Clients => new Proxies();

        public IGroupManager Groups => throw new NotSupportedException();

        private sealed class Proxies : HubClientsBase, IHubClients<INotificationsClient>
        {
            public override INotificationsClient User(string userId) => new Client();
        }

        private sealed class Client : INotificationsClient
        {
            public Task NotificationCreated(NotificationDto notification) =>
                throw new IOException("The SignalR transport is unavailable.");
        }
    }

    /// <summary>
    /// <see cref="IHubClients{T}"/> has a dozen members and
    /// <see cref="NotificationService"/> uses exactly one. Everything else
    /// throws, so a future push through a different channel fails the test
    /// rather than passing silently.
    /// </summary>
    private abstract class HubClientsBase
    {
        private static NotSupportedException Unused([System.Runtime.CompilerServices.CallerMemberName] string member = "") =>
            new($"{member} is not used by NotificationService.");

        public INotificationsClient All => throw Unused();

        public INotificationsClient AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw Unused();

        public INotificationsClient Client(string connectionId) => throw Unused();

        public INotificationsClient Clients(IReadOnlyList<string> connectionIds) => throw Unused();

        public INotificationsClient Group(string groupName) => throw Unused();

        public INotificationsClient Groups(IReadOnlyList<string> groupNames) => throw Unused();

        public INotificationsClient GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw Unused();

        public abstract INotificationsClient User(string userId);

        public INotificationsClient Users(IReadOnlyList<string> userIds) => throw Unused();
    }
}
