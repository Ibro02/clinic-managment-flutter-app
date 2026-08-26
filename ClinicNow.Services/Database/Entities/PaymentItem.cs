namespace ClinicNow.Services.Database.Entities;

/// <summary>
/// A priced line item under a <see cref="Payment"/> - mirrors the reference
/// repo's `Uplata`/`StavkaUplate` split (design doc §3). Today always exactly
/// one item per payment (the appointment's own service), but keeps the shape
/// the plan calls for rather than flattening the amount onto `Payment` itself.
/// </summary>
public class PaymentItem
{
    public int Id { get; set; }

    public int PaymentId { get; set; }

    public Payment Payment { get; set; } = null!;

    public int MedicalServiceId { get; set; }

    public MedicalService MedicalService { get; set; } = null!;

    public string Description { get; set; } = string.Empty;

    public decimal AmountEur { get; set; }
}
