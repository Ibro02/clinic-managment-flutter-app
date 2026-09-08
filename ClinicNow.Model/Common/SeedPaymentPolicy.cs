namespace ClinicNow.Model.Common;

/// <summary>
/// Seeded demo payments (see <c>PaymentConfiguration.HasData</c>) carry synthetic
/// PayPal capture ids so the app is demoable on a clean DB, but no real PayPal
/// transaction backs them - a refund against one always fails on the live API.
/// Both <c>AppointmentService</c> (surfacing the disabled-with-reason UI state,
/// rulebook Part II §K) and <c>PaymentService</c> (rejecting the refund
/// server-side, so a direct API call can't bypass the UI) key off this one
/// prefix/message pair rather than each hardcoding their own copy.
/// </summary>
public static class SeedPaymentPolicy
{
    public const string CapturePrefix = "SEED-";

    public const string RefundBlockedReason =
        "Demo zapis — povrat nije moguć jer iza njega ne stoji stvarna PayPal transakcija. " +
        "Za demonstraciju povrata platite termin kroz mobilnu aplikaciju.";

    public static bool IsSeeded(string? payPalCaptureId) =>
        payPalCaptureId is not null && payPalCaptureId.StartsWith(CapturePrefix, StringComparison.Ordinal);
}
