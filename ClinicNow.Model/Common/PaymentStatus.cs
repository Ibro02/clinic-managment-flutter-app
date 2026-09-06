namespace ClinicNow.Model.Common;

/// <summary>
/// A `Payment`'s lifecycle. Fully decoupled from `AppointmentStatus` - paying
/// or refunding never changes the appointment's own state machine, and vice
/// versa (design doc §2/§9). `Pending` covers a created-but-not-yet-captured
/// PayPal order; there is still no "Failed" state, because a capture PayPal
/// refuses is a retryable non-event rather than a terminal outcome.
///
/// `Cancelled` (review item C12) is what an *abandoned* attempt becomes: the
/// patient closing the PayPal WebView unapproved, or a stale attempt being
/// superseded when a new one starts. It exists because leaving those rows
/// `Pending` forever is what let one appointment accumulate several
/// simultaneously-capturable payments - the double-charge risk C12 is about.
/// A `Cancelled` row can never be captured and is invisible to
/// "what is this appointment's payment" lookups.
/// </summary>
public enum PaymentStatus
{
    Pending = 0,
    Paid = 1,
    PartiallyRefunded = 2,
    Refunded = 3,
    Cancelled = 4,

    /// <summary>
    /// Money moved, but PayPal reported capturing an amount that disagrees with
    /// what was ordered (review item C13a). Deliberately not `Paid`: the capture
    /// is real and must never be retried, yet it isn't a clean success either
    /// and needs a human to reconcile it. Counts as "already paid" everywhere
    /// that guards against double-charging, and stays refundable - capped by
    /// what was actually captured.
    /// </summary>
    RequiresReconciliation = 5
}

public static class PaymentStatusExtensions
{
    public static string ToDisplayName(this PaymentStatus status) => status switch
    {
        PaymentStatus.Pending => "Na čekanju",
        PaymentStatus.Paid => "Plaćeno",
        PaymentStatus.PartiallyRefunded => "Djelomično vraćeno",
        PaymentStatus.Refunded => "Vraćeno",
        PaymentStatus.Cancelled => "Otkazano",
        PaymentStatus.RequiresReconciliation => "Neusklađen iznos",
        _ => status.ToString()
    };
}
