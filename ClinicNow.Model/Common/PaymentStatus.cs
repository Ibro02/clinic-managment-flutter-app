namespace ClinicNow.Model.Common;

/// <summary>
/// A `Payment`'s lifecycle. Fully decoupled from `AppointmentStatus` - paying
/// or refunding never changes the appointment's own state machine, and vice
/// versa (design doc §2/§9). `Pending` covers a created-but-not-yet-captured
/// PayPal order; there is no separate "Failed" state - a capture that never
/// succeeds just leaves the row `Pending`, and the patient simply retries
/// (which creates a fresh `Payment`, per design doc §3).
/// </summary>
public enum PaymentStatus
{
    Pending = 0,
    Paid = 1,
    PartiallyRefunded = 2,
    Refunded = 3
}

public static class PaymentStatusExtensions
{
    public static string ToDisplayName(this PaymentStatus status) => status switch
    {
        PaymentStatus.Pending => "Na čekanju",
        PaymentStatus.Paid => "Plaćeno",
        PaymentStatus.PartiallyRefunded => "Djelomično vraćeno",
        PaymentStatus.Refunded => "Vraćeno",
        _ => status.ToString()
    };
}
