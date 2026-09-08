using System.Globalization;
using ClinicNow.Model.Configuration;
using ClinicNow.Model.Exceptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using PaypalServerSdk.Standard;
using PaypalServerSdk.Standard.Authentication;
using PaypalServerSdk.Standard.Exceptions;
using PaypalServerSdk.Standard.Models;

namespace ClinicNow.Services.Payments;

/// <summary>
/// Wraps the official PayPal Server SDK (review item C13: no more hand-rolled
/// REST calls over `IHttpClientFactory`). The SDK client itself owns OAuth token
/// acquisition and refresh; what this class caches in <see cref="IMemoryCache"/>
/// is the built, authenticated <see cref="PaypalServerSdkClient"/> instance so
/// that caching survives across this (Scoped) service's per-request lifetime
/// instead of re-running the OAuth handshake on every call.
/// </summary>
public class PayPalClient : IPayPalClient
{
    private const string SdkClientCacheKey = "paypal:sdk_client";

    /// <summary>
    /// Well under the SDK's ~100s default: a PayPal call also happens inside
    /// AppointmentService.CancelAsync's automatic-refund path, so a hung PayPal
    /// endpoint must not be able to stall a cancellation request for a minute
    /// and a half.
    /// </summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    private readonly IMemoryCache _cache;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalClient> _logger;

    public PayPalClient(IMemoryCache cache, PayPalOptions options, ILogger<PayPalClient> logger)
    {
        _cache = cache;
        _options = options;
        _logger = logger;
    }

    public async Task<(string OrderId, string ApproveUrl)> CreateOrderAsync(decimal amountEur, string returnUrl, string cancelUrl, CancellationToken cancellationToken = default)
    {
        var client = GetSdkClient();

        var input = new CreateOrderInput
        {
            Body = new OrderRequest
            {
                Intent = CheckoutPaymentIntent.Capture,
                PurchaseUnits =
                [
                    new PurchaseUnitRequest
                    {
                        Amount = new AmountWithBreakdown
                        {
                            CurrencyCode = "EUR",
                            MValue = amountEur.ToString("F2", CultureInfo.InvariantCulture)
                        }
                    }
                ],
                ApplicationContext = new OrderApplicationContext
                {
                    ReturnUrl = returnUrl,
                    CancelUrl = cancelUrl,
                    UserAction = OrderApplicationContextUserAction.PayNow
                }
            }
        };

        Order order;
        try
        {
            var response = await client.OrdersController.CreateOrderAsync(input, cancellationToken);
            order = response.Data;
        }
        catch (ApiException ex)
        {
            var detail = LogPayPalError("CreateOrder", ex);
            throw new BusinessException($"Plaćanje trenutno nije moguće ({detail}). Pokušajte ponovo kasnije.");
        }

        var approveUrl = order.Links?.FirstOrDefault(l => l.Rel == "approve")?.Href
            ?? throw new BusinessException("PayPal nije vratio link za odobravanje plaćanja.");

        return (order.Id!, approveUrl);
    }

    public async Task<(string? CaptureId, decimal CapturedAmountEur, bool Success)> CaptureOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        var client = GetSdkClient();

        Order order;
        try
        {
            var response = await client.OrdersController.CaptureOrderAsync(
                new CaptureOrderInput { Id = orderId }, cancellationToken);
            order = response.Data;
        }
        catch (ApiException ex)
        {
            // A capture on an order the buyer never approved is an EXPECTED
            // failure (e.g. the patient closed the WebView without approving)
            // - log it, but return Success=false rather than throwing, so
            // PaymentService can leave the Payment row Pending for a retry
            // instead of surfacing a scary 500.
            LogPayPalError("CaptureOrder", ex, orderId);
            return (null, 0m, false);
        }

        var captured = order.PurchaseUnits?.FirstOrDefault()?.Payments?.Captures?.FirstOrDefault();

        if (captured is null || captured.Status != CaptureStatus.Completed)
        {
            // A 2xx response here is a *successful* PayPal response, so it
            // carries the payer's email/account id - log only the capture
            // status, never the full response.
            _logger.LogWarning("PayPal CaptureOrder for order {OrderId} did not complete (capture status: {CaptureStatus}).",
                orderId, captured?.Status?.ToString() ?? "(no capture returned)");
            return (null, 0m, false);
        }

        var capturedAmount = decimal.Parse(captured.Amount!.MValue, CultureInfo.InvariantCulture);
        return (captured.Id, capturedAmount, true);
    }

    public async Task<string> RefundCaptureAsync(string captureId, decimal amountEur, string reason, CancellationToken cancellationToken = default)
    {
        var client = GetSdkClient();

        var input = new RefundCapturedPaymentInput
        {
            CaptureId = captureId,
            Body = new RefundRequest
            {
                Amount = new Money
                {
                    CurrencyCode = "EUR",
                    MValue = amountEur.ToString("F2", CultureInfo.InvariantCulture)
                },
                NoteToPayer = reason
            }
        };

        Refund refund;
        try
        {
            var response = await client.PaymentsController.RefundCapturedPaymentAsync(input, cancellationToken);
            refund = response.Data;
        }
        catch (ApiException ex)
        {
            var detail = LogPayPalError("RefundCapture", ex, captureId);
            throw new BusinessException($"Povrat sredstava trenutno nije moguć ({detail}). Pokušajte ponovo kasnije.");
        }

        return refund.Id!;
    }

    // --- diagnostics -----------------------------------------------------------

    /// <summary>
    /// Logs, and returns, only the SDK's already-parsed diagnostic fields
    /// (<c>Name</c>, <c>DebugId</c>, <c>Details[].Issue</c> for an
    /// <see cref="ErrorException"/>, or <c>Error</c>/<c>ErrorDescription</c> for
    /// an <see cref="OAuthProviderException"/>) - never the raw response body,
    /// which carries the payer's real email address and PayPal account id on
    /// capture/refund responses and has no business in application logs.
    ///
    /// The return value is folded into the <see cref="BusinessException"/> the
    /// caller throws: collapsing every possible PayPal failure (invalid
    /// resource, permission, currency mismatch, already refunded, ...) into one
    /// identical generic sentence made a real failure undiagnosable from the UI
    /// - staff had no way to tell "PayPal rejected this" from "the capture id
    /// was never real" without server-log access.
    /// </summary>
    private string LogPayPalError(string operation, ApiException ex, string? subjectId = null)
    {
        var description = ex switch
        {
            ErrorException error => DescribeErrorException(error),
            OAuthProviderException oauth => $"error={oauth.Error}; error_description={oauth.ErrorDescription}",
            _ => ex.Message
        };

        if (subjectId is null)
        {
            _logger.LogWarning("PayPal {Operation} failed ({Status}): {Error}", operation, ex.ResponseCode, description);
        }
        else
        {
            _logger.LogWarning("PayPal {Operation} failed for {SubjectId} ({Status}): {Error}", operation, subjectId, ex.ResponseCode, description);
        }

        return description;
    }

    private static string DescribeErrorException(ErrorException error)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(error.Name)) parts.Add($"name={error.Name}");
        if (!string.IsNullOrEmpty(error.DebugId)) parts.Add($"debug_id={error.DebugId}");

        var issues = error.Details?
            .Select(d => d.Issue)
            .Where(i => !string.IsNullOrEmpty(i)) ?? [];
        var joined = string.Join(",", issues);
        if (joined.Length > 0) parts.Add($"issues={joined}");

        return parts.Count > 0 ? string.Join("; ", parts) : error.Message;
    }

    // --- SDK client --------------------------------------------------------------

    /// <summary>
    /// Builds the SDK client once and reuses it for the process lifetime (via
    /// <see cref="IMemoryCache"/>, since this service itself is registered
    /// Scoped): the client owns the HTTP connection and the OAuth token cache
    /// internally, so rebuilding it per request would mean re-authenticating
    /// with PayPal on every call.
    /// </summary>
    private PaypalServerSdkClient GetSdkClient()
    {
        if (_cache.TryGetValue(SdkClientCacheKey, out PaypalServerSdkClient? cached) && cached is not null)
        {
            return cached;
        }

        if (string.IsNullOrEmpty(_options.ClientId) || string.IsNullOrEmpty(_options.ClientSecret))
        {
            throw new BusinessException("PayPal integracija nije konfigurisana (nedostaje PAYPAL_CLIENT_ID/PAYPAL_CLIENT_SECRET).");
        }

        var client = new PaypalServerSdkClient.Builder()
            .ClientCredentialsAuth(
                new ClientCredentialsAuthModel.Builder(_options.ClientId, _options.ClientSecret).Build())
            .Environment(_options.Mode.Equals("live", StringComparison.OrdinalIgnoreCase)
                ? PaypalServerSdk.Standard.Environment.Production
                : PaypalServerSdk.Standard.Environment.Sandbox)
            .HttpClientConfig(config => config.Timeout(RequestTimeout))
            .Build();

        // Never expires: the SDK client (and its internal OAuth token cache)
        // is meant to live for the process lifetime, same as a singleton
        // HttpClient would.
        _cache.Set(SdkClientCacheKey, client);
        return client;
    }
}
