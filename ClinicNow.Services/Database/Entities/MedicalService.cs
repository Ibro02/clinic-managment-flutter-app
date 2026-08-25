namespace ClinicNow.Services.Database.Entities;

/// <summary>
/// A billable clinical service (e.g. "Pregled kod dermatologa") - reference table/
/// codebook (CLAUDE.md §6). <c>Appointment</c> (Phase 4) links to exactly one of
/// these; <c>Payment</c>/<c>PaymentItem</c> (Phase 8) price off it. The price lives
/// here, server-side, as the single source of truth - never trust a client-supplied
/// amount (rulebook Part II §J).
/// </summary>
public class MedicalService
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public decimal Price { get; set; }

    public int DurationMinutes { get; set; }
}
