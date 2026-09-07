using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicNow.Model.Configuration;
using ClinicNow.Model.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNow.Worker.Push;

/// <summary>
/// Sends notifications through Firebase Cloud Messaging, HTTP v1.
///
/// v1 is not a preference: the legacy <c>/fcm/send</c> endpoint and its static
/// server key were switched off in June 2024, so authentication is necessarily a
/// short-lived OAuth2 access token minted from the service-account key. That is
/// why the credential is a JSON document in <c>.env</c> rather than a single
/// string, and why <see cref="GoogleCredential"/> - not a hand-rolled JWT signer
/// - mints and caches the token.
///
/// One HTTP request per token, because the v1 API dropped the legacy multicast
/// endpoint. A clinic notification goes to the one or two devices a person
/// actually owns, so the loop is small; the alternative (<c>/batch</c>) buys
/// nothing at this size and is itself deprecated.
/// </summary>
public class FcmPushSender : IPushSender
{
    private const string MessagingScope = "https://www.googleapis.com/auth/firebase.messaging";

    private readonly FirebaseOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<FcmPushSender> _logger;

    /// <summary>
    /// Built once from the configured credential. <see cref="ITokenAccess"/>
    /// caches the access token internally and refreshes it shortly before
    /// expiry, so this is not a per-send round trip to Google's token endpoint.
    /// </summary>
    private readonly ITokenAccess? _credential;

    public FcmPushSender(FirebaseOptions options, IHttpClientFactory httpClientFactory, ILogger<FcmPushSender> logger)
    {
        _options = options;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        _credential = options.IsConfigured
            ? GoogleCredential.FromJson(options.ServiceAccountJson).CreateScoped(MessagingScope)
            : null;
    }

    public async Task SendAsync(PushMessage message, CancellationToken cancellationToken)
    {
        if (_credential is null)
        {
            // Not an error: push is an enhancement over an in-app notification
            // that is already persisted and already delivered. A developer with
            // no Firebase credentials must still be able to run the stack, so
            // this states the reason once per message rather than throwing and
            // burning four retries on a condition retrying cannot fix.
            _logger.LogInformation(
                "Firebase is not configured (FIREBASE_PROJECT_ID / FIREBASE_SERVICE_ACCOUNT_BASE64); " +
                "skipping push for notification {NotificationId}.", message.NotificationId);
            return;
        }

        var accessToken = await _credential.GetAccessTokenForRequestAsync(cancellationToken: cancellationToken);
        var endpoint = $"https://fcm.googleapis.com/v1/projects/{_options.ProjectId}/messages:send";

        using var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        foreach (var token in message.Tokens)
        {
            await SendToTokenAsync(client, endpoint, token, message, cancellationToken);
        }
    }

    private async Task SendToTokenAsync(
        HttpClient client,
        string endpoint,
        string token,
        PushMessage message,
        CancellationToken cancellationToken)
    {
        // A `notification` block (rather than data-only) is what makes Android
        // display this from the system tray while the app is closed - which is
        // the entire reason this feature exists. `data` rides alongside so a tap
        // can open the right notification once the app is running.
        var payload = new
        {
            message = new
            {
                token,
                notification = new { title = message.Title, body = message.Body },
                data = new { notificationId = message.NotificationId.ToString() },
                android = new { priority = "high", notification = new { sound = "default" } }
            }
        };

        using var response = await client.PostAsJsonAsync(endpoint, payload, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("Delivered notification {NotificationId} to a device.", message.NotificationId);
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        // A token belonging to an uninstalled app is permanently dead. Retrying
        // it would fail identically four more times and then log an error that
        // reads like an outage, so it is logged as the routine event it is and
        // the remaining tokens still get their push.
        if (IsPermanentlyInvalid(response.StatusCode, body))
        {
            _logger.LogInformation(
                "A device token is no longer registered (app uninstalled or token rotated); skipping it for notification {NotificationId}.",
                message.NotificationId);
            return;
        }

        // Everything else - 401/403 (credential wrong), 429, 5xx, transport - is
        // worth another attempt, so it propagates to the consumer's retry.
        throw new HttpRequestException(
            $"FCM rejected a push for notification {message.NotificationId} with {(int)response.StatusCode}: {Truncate(body)}");
    }

    /// <summary>
    /// FCM signals a dead token as 404 NOT_FOUND (UNREGISTERED), or as 400
    /// INVALID_ARGUMENT when the string is not a token at all. Both are matched
    /// on the documented error code in the body rather than the status alone,
    /// since 400 also covers malformed payloads - which are our bug, and must
    /// not be quietly written off as a stale device.
    /// </summary>
    private static bool IsPermanentlyInvalid(HttpStatusCode statusCode, string body)
    {
        if (statusCode == HttpStatusCode.NotFound)
        {
            return true;
        }

        if (statusCode != HttpStatusCode.BadRequest)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("details", out var details)
                && details.EnumerateArray().Any(detail =>
                    detail.TryGetProperty("errorCode", out var code)
                    && code.GetString() is "UNREGISTERED" or "INVALID_ARGUMENT");
        }
        catch (JsonException)
        {
            // An unparseable body is not evidence of a dead token - treat it as
            // retryable rather than silently dropping a real notification.
            return false;
        }
    }

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500] + "...";
}
