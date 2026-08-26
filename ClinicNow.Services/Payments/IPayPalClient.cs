namespace ClinicNow.Services.Payments;

/// <summary>
/// Thin wrapper over PayPal's REST Orders v2 API (design doc §4). No PayPal
/// SDK package - a handful of well-understood REST calls via `IHttpClientFactory`
/// is simpler and matches CLAUDE.md's "wrap external API calls in a clean
/// service layer" rule better than an opaque third-party client.
/// </summary>
public interface IPayPalClient
{
    /// <summary>Creates a PayPal order for the given EUR amount; returns the order id and the hosted approval URL the client must open.</summary>
    Task<(string OrderId, string ApproveUrl)> CreateOrderAsync(decimal amountEur, string returnUrl, string cancelUrl, CancellationToken cancellationToken = default);

    /// <summary>Captures a previously-approved order. `Success=false` means PayPal itself reports the capture didn't complete (e.g. the buyer never approved it) - never an exception for that specific, expected case.</summary>
    Task<(string? CaptureId, decimal CapturedAmountEur, bool Success)> CaptureOrderAsync(string orderId, CancellationToken cancellationToken = default);

    /// <summary>Refunds part or all of a capture.</summary>
    Task<string> RefundCaptureAsync(string captureId, decimal amountEur, string reason, CancellationToken cancellationToken = default);
}
