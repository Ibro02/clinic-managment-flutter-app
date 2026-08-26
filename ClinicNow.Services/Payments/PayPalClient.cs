using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClinicNow.Model.Configuration;
using ClinicNow.Model.Exceptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace ClinicNow.Services.Payments;

public class PayPalClient : IPayPalClient
{
    private const string TokenCacheKey = "paypal:access_token";

    /// <summary>
    /// Well under <see cref="HttpClient"/>'s ~100s default: a PayPal call also
    /// happens inside AppointmentService.CancelAsync's automatic-refund path, so
    /// a hung PayPal endpoint must not be able to stall a cancellation request
    /// for a minute and a half.
    /// </summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalClient> _logger;

    public PayPalClient(IHttpClientFactory httpClientFactory, IMemoryCache cache, PayPalOptions options, ILogger<PayPalClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _options = options;
        _logger = logger;
    }

    public async Task<(string OrderId, string ApproveUrl)> CreateOrderAsync(decimal amountEur, string returnUrl, string cancelUrl, CancellationToken cancellationToken = default)
    {
        var client = await CreateAuthorizedClientAsync(cancellationToken);

        var payload = new
        {
            intent = "CAPTURE",
            purchase_units = new[]
            {
                new { amount = new { currency_code = "EUR", value = amountEur.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) } }
            },
            application_context = new { return_url = returnUrl, cancel_url = cancelUrl, user_action = "PAY_NOW" }
        };

        var response = await client.PostAsJsonAsync("/v2/checkout/orders", payload, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("PayPal CreateOrder failed ({Status}): {Error}", response.StatusCode, DescribeError(body));
            throw new BusinessException("Plaćanje trenutno nije moguće. Pokušajte ponovo kasnije.");
        }

        var order = JsonSerializer.Deserialize<PayPalOrderResponse>(body)
            ?? throw new BusinessException("Plaćanje trenutno nije moguće. Pokušajte ponovo kasnije.");

        var approveUrl = order.Links.FirstOrDefault(l => l.Rel == "approve")?.Href
            ?? throw new BusinessException("PayPal nije vratio link za odobravanje plaćanja.");

        return (order.Id, approveUrl);
    }

    public async Task<(string? CaptureId, decimal CapturedAmountEur, bool Success)> CaptureOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        var client = await CreateAuthorizedClientAsync(cancellationToken);

        var response = await client.PostAsync($"/v2/checkout/orders/{orderId}/capture",
            new StringContent("{}", Encoding.UTF8, "application/json"), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // A capture on an order the buyer never approved is an EXPECTED
            // failure (e.g. the patient closed the WebView without approving)
            // - log it, but return Success=false rather than throwing, so
            // PaymentService can leave the Payment row Pending for a retry
            // instead of surfacing a scary 500.
            _logger.LogWarning("PayPal CaptureOrder failed for order {OrderId} ({Status}): {Error}", orderId, response.StatusCode, DescribeError(body));
            return (null, 0m, false);
        }

        var capture = JsonSerializer.Deserialize<PayPalCaptureResponse>(body);
        var captured = capture?.PurchaseUnits.FirstOrDefault()?.Payments?.Captures.FirstOrDefault();

        if (captured is null || !string.Equals(captured.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            // A 2xx body here is a *successful* PayPal response, so it carries
            // the payer's email/account id - log only the capture status.
            _logger.LogWarning("PayPal CaptureOrder for order {OrderId} did not complete (capture status: {CaptureStatus}).",
                orderId, captured?.Status ?? "(no capture returned)");
            return (null, 0m, false);
        }

        var capturedAmount = decimal.Parse(captured.Amount.Value, System.Globalization.CultureInfo.InvariantCulture);
        return (captured.Id, capturedAmount, true);
    }

    public async Task<string> RefundCaptureAsync(string captureId, decimal amountEur, string reason, CancellationToken cancellationToken = default)
    {
        var client = await CreateAuthorizedClientAsync(cancellationToken);

        var payload = new
        {
            amount = new { value = amountEur.ToString("F2", System.Globalization.CultureInfo.InvariantCulture), currency_code = "EUR" },
            note_to_payer = reason
        };

        var response = await client.PostAsJsonAsync($"/v2/payments/captures/{captureId}/refund", payload, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("PayPal RefundCapture failed for capture {CaptureId} ({Status}): {Error}", captureId, response.StatusCode, DescribeError(body));
            throw new BusinessException("Povrat sredstava trenutno nije moguć. Pokušajte ponovo kasnije.");
        }

        var refund = JsonSerializer.Deserialize<PayPalRefundResponse>(body)
            ?? throw new BusinessException("Povrat sredstava trenutno nije moguć. Pokušajte ponovo kasnije.");

        return refund.Id;
    }

    // --- diagnostics -----------------------------------------------------------

    /// <summary>
    /// Condenses a PayPal error/response body into just its diagnostic fields
    /// (<c>name</c>, <c>debug_id</c>, <c>details[].issue</c>, or the OAuth
    /// <c>error</c>/<c>error_description</c> pair) for logging.
    ///
    /// Deliberately never logs the raw body: capture and refund payloads carry
    /// the payer's real email address and PayPal account id, and application
    /// logs are not the place for third-party PII. The fields extracted here are
    /// the ones actually needed to diagnose a failure, and none of them identify
    /// the payer. If the body isn't parseable JSON, only its length is reported
    /// - a truncated prefix could still contain PII.
    /// </summary>
    private static string DescribeError(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "(empty body)";

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return $"(non-object body, {body.Length} chars)";

            var parts = new List<string>();

            foreach (var field in new[] { "name", "error", "error_description", "debug_id" })
            {
                if (root.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String)
                {
                    parts.Add($"{field}={value.GetString()}");
                }
            }

            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                var issues = details.EnumerateArray()
                    .Where(d => d.ValueKind == JsonValueKind.Object && d.TryGetProperty("issue", out _))
                    .Select(d => d.GetProperty("issue").GetString())
                    .Where(i => !string.IsNullOrEmpty(i));
                var joined = string.Join(",", issues);
                if (joined.Length > 0) parts.Add($"issues={joined}");
            }

            return parts.Count > 0 ? string.Join("; ", parts) : $"(no diagnostic fields, {body.Length} chars)";
        }
        catch (JsonException)
        {
            return $"(unparseable body, {body.Length} chars)";
        }
    }

    // --- auth -----------------------------------------------------------------

    private async Task<HttpClient> CreateAuthorizedClientAsync(CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(_options.BaseUrl);
        client.Timeout = RequestTimeout;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync(cancellationToken));
        return client;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(TokenCacheKey, out string? cachedToken) && cachedToken is not null)
        {
            return cachedToken;
        }

        if (string.IsNullOrEmpty(_options.ClientId) || string.IsNullOrEmpty(_options.ClientSecret))
        {
            throw new BusinessException("PayPal integracija nije konfigurisana (nedostaje PAYPAL_CLIENT_ID/PAYPAL_CLIENT_SECRET).");
        }

        var client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(_options.BaseUrl);
        client.Timeout = RequestTimeout;

        var basicAuth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token")
        {
            Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("grant_type", "client_credentials")])
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);

        var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("PayPal OAuth token request failed ({Status}): {Error}", response.StatusCode, DescribeError(body));
            throw new BusinessException("PayPal integracija trenutno nije dostupna.");
        }

        var token = JsonSerializer.Deserialize<PayPalTokenResponse>(body)
            ?? throw new BusinessException("PayPal integracija trenutno nije dostupna.");

        // Cache until shortly before real expiry so a request never races an
        // about-to-expire token.
        _cache.Set(TokenCacheKey, token.AccessToken, TimeSpan.FromSeconds(Math.Max(60, token.ExpiresIn - 60)));
        return token.AccessToken;
    }
}
