using ClinicNow.Model.Common;
using ClinicNow.Model.Messaging;
using ClinicNow.Services.Database;
using ClinicNow.Services.Messaging;
using ClinicNow.Services.Notifications;
using Microsoft.EntityFrameworkCore;

namespace ClinicNow.API;

/// <summary>
/// Periodically scans Confirmed appointments starting within the next ~24h that
/// haven't already had a reminder sent, and enqueues a reminder email + in-app
/// notification for each (CLAUDE.md §9: "Use cases: ... pre-appointment
/// reminder"). Lives in ClinicNow.API (not the Worker) because it needs
/// <see cref="ClinicNowContext"/> - the Worker has no DB access wired
/// (ClinicNow.Worker.csproj only references ClinicNow.Model).
///
/// A singleton <see cref="BackgroundService"/> needs a fresh <em>scoped</em>
/// DbContext per tick, hence <see cref="IServiceScopeFactory"/> rather than
/// injecting <see cref="ClinicNowContext"/> directly.
/// </summary>
public class PreAppointmentReminderHostedService : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ReminderWindow = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PreAppointmentReminderHostedService> _logger;

    public PreAppointmentReminderHostedService(IServiceScopeFactory scopeFactory, ILogger<PreAppointmentReminderHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanAndSendRemindersAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // A scan failure must never crash the API host - log loudly and
                // retry on the next tick (rulebook Appendix A.1: no silent
                // failures, but also don't take the whole API down over this).
                _logger.LogError(ex, "Pre-appointment reminder scan failed.");
            }

            await Task.Delay(ScanInterval, stoppingToken);
        }
    }

    private async Task ScanAndSendRemindersAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ClinicNowContext>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var emailPublisher = scope.ServiceProvider.GetRequiredService<IEmailPublisher>();

        var now = DateTime.UtcNow;
        var windowEnd = now.Add(ReminderWindow);

        var upcoming = await context.Appointments
            .Include(a => a.Patient).ThenInclude(p => p!.User)
            .Include(a => a.Doctor).ThenInclude(d => d.User)
            .Where(a => a.Status == AppointmentStatus.Confirmed
                        && a.ReminderSentAtUtc == null
                        && a.StartUtc > now
                        && a.StartUtc <= windowEnd)
            .ToListAsync(cancellationToken);

        foreach (var appointment in upcoming)
        {
            var doctorName = $"{appointment.Doctor.User.FirstName} {appointment.Doctor.User.LastName}";
            var text = $"Podsjetnik: imate zakazan termin kod dr. {doctorName} za {appointment.StartUtc:dd.MM.yyyy HH:mm} UTC.";

            if (appointment.Patient.UserId is int patientUserId)
            {
                await notificationService.CreateAsync(patientUserId, "Podsjetnik za termin", text, cancellationToken);
            }

            if (appointment.Patient.User is not null)
            {
                await emailPublisher.PublishAsync(new EmailMessage
                {
                    To = appointment.Patient.User.Email,
                    Subject = "ClinicNow - podsjetnik za termin",
                    Body = text
                }, cancellationToken);
            }

            appointment.ReminderSentAtUtc = now;
        }

        if (upcoming.Count > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Sent {Count} pre-appointment reminder(s).", upcoming.Count);
        }
    }
}
