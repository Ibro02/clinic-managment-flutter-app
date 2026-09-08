namespace ClinicNow.Model.Dto;

/// <summary>
/// One appointment status transition - who did it, when (UTC), and why
/// (rulebook §7's audit trail: "ko je odobrio ili odbio zahtjev, kada je to
/// učinjeno i odgovarajući opis"). Only ever populated on
/// <see cref="AppointmentDto.AuditLogs"/> via the detail endpoint - never the
/// paged list endpoint (rulebook Part II §8.2: list DTOs stay display-only).
/// </summary>
public class AppointmentAuditLogDto
{
    public int Id { get; set; }

    public string ActingUserName { get; set; } = string.Empty;

    public Common.AppointmentStatus Status { get; set; }

    public string StatusName { get; set; } = string.Empty;

    public DateTime OccurredAtUtc { get; set; }

    public string? Description { get; set; }
}
