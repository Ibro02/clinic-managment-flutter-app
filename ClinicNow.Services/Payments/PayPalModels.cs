using System.Text.Json.Serialization;

namespace ClinicNow.Services.Payments;

internal class PayPalTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

internal class PayPalMoney
{
    [JsonPropertyName("currency_code")]
    public string CurrencyCode { get; set; } = "EUR";

    [JsonPropertyName("value")]
    public string Value { get; set; } = "0.00";
}

internal class PayPalLink
{
    [JsonPropertyName("href")]
    public string Href { get; set; } = string.Empty;

    [JsonPropertyName("rel")]
    public string Rel { get; set; } = string.Empty;
}

internal class PayPalOrderResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("links")]
    public List<PayPalLink> Links { get; set; } = [];
}

internal class PayPalCaptureResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("purchase_units")]
    public List<PayPalPurchaseUnit> PurchaseUnits { get; set; } = [];
}

internal class PayPalPurchaseUnit
{
    [JsonPropertyName("payments")]
    public PayPalPayments? Payments { get; set; }
}

internal class PayPalPayments
{
    [JsonPropertyName("captures")]
    public List<PayPalCapture> Captures { get; set; } = [];
}

internal class PayPalCapture
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public PayPalMoney Amount { get; set; } = new();
}

internal class PayPalRefundResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}
