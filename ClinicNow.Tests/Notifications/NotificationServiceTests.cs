using ClinicNow.Model.Messaging;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using ClinicNow.Services.Messaging;
using ClinicNow.Services.Notifications;
using ClinicNow.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClinicNow.Tests.Notifications;

/// <summary>
/// The device push (review item C15's "reaches the user with the app closed"
/// half) is best-effort - a broker that cannot deliver must never turn an
/// operation that already committed - a booking, a cancellation, a captured
/// payment - into an error for the caller. There is no SignalR hub push
/// anymore: <c>NotificationsHub</c> had no Flutter client ever connecting to
/// it, so it was dead code rather than a working feature (review item C4) -
/// both clients get their auto-refresh from <c>NotificationCenter</c> polling
/// this same list, which the rulebook explicitly permits as an alternative.
/// </summary>
public class NotificationServiceTests
{
    private const int SeededUserId = 1;

    private static NotificationService CreateService(
        ClinicNowContext context,
        IPushPublisher? pushPublisher = null) =>
        new(
            context,
            TestContextFactory.CreateMapper(),
            TestContextFactory.CreateHttpContextAccessor(SeededUserId),
            pushPublisher ?? new RecordingPushPublisher(),
            NullLogger<NotificationService>.Instance);

    private static async Task AddDeviceTokenAsync(ClinicNowContext context, int userId, string token)
    {
        context.DeviceTokens.Add(new DeviceToken
        {
            UserId = userId,
            Token = token,
            Platform = "Android",
            CreatedAtUtc = DateTime.UtcNow,
            LastSeenAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task CreateAsync_persists_the_notification()
    {
        using var context = TestContextFactory.CreateContext();
        var service = CreateService(context);

        await service.CreateAsync(SeededUserId, "Termin potvrđen", "Vaš termin je potvrđen.");

        var saved = await context.Notifications.SingleAsync(n => n.Title == "Termin potvrđen");
        Assert.Equal(SeededUserId, saved.UserId);
        Assert.False(saved.IsRead);
    }

    [Fact]
    public async Task CreateAsync_queues_a_device_push_for_every_registered_device()
    {
        using var context = TestContextFactory.CreateContext();
        await AddDeviceTokenAsync(context, SeededUserId, "token-phone");
        await AddDeviceTokenAsync(context, SeededUserId, "token-tablet");

        var push = new RecordingPushPublisher();
        var service = CreateService(context, push);

        await service.CreateAsync(SeededUserId, "Termin potvrđen", "Vaš termin je potvrđen.");

        var message = Assert.Single(push.Published);
        Assert.Equal(["token-phone", "token-tablet"], [.. message.Tokens]);
        Assert.Equal("Termin potvrđen", message.Title);
        // Carried so a tapped notification can open the right row, and so a
        // delivery in the Worker's log can be traced back to its cause.
        Assert.NotEqual(0, message.NotificationId);
    }

    [Fact]
    public async Task CreateAsync_does_not_push_to_another_users_device()
    {
        using var context = TestContextFactory.CreateContext();
        await AddDeviceTokenAsync(context, userId: 2, token: "someone-elses-phone");

        var push = new RecordingPushPublisher();
        var service = CreateService(context, push);

        await service.CreateAsync(SeededUserId, "Termin potvrđen", "Vaš termin je potvrđen.");

        // A medical notification reaching the wrong device is the worst failure
        // this feature can have, so it gets its own test rather than being
        // implied by the query.
        Assert.Empty(push.Published);
    }

    [Fact]
    public async Task CreateAsync_still_persists_when_queueing_the_device_push_fails()
    {
        using var context = TestContextFactory.CreateContext();
        await AddDeviceTokenAsync(context, SeededUserId, "token-phone");

        var service = CreateService(context, new ThrowingPushPublisher());

        await service.CreateAsync(SeededUserId, "Termin otkazan", "Vaš termin je otkazan.");

        var saved = await context.Notifications.SingleAsync(n => n.Title == "Termin otkazan");
        Assert.False(saved.IsRead);
    }

    private sealed class RecordingPushPublisher : IPushPublisher
    {
        public List<PushMessage> Published { get; } = [];

        public Task<bool> PublishAsync(PushMessage message, CancellationToken cancellationToken)
        {
            Published.Add(message);
            return Task.FromResult(true);
        }
    }

    /// <summary>Stands in for a broker that is refusing connections outright.</summary>
    private sealed class ThrowingPushPublisher : IPushPublisher
    {
        public Task<bool> PublishAsync(PushMessage message, CancellationToken cancellationToken) =>
            throw new IOException("Broker unreachable.");
    }
}
