namespace ClinicNow.Model.Common;

/// <summary>
/// PayPal doesn't support BAM (Bosnian convertible mark), so every charge is
/// made in EUR, converted server-side from `MedicalService.Price` (design doc
/// §2/§4 - "server owns the price catalog", never a client-supplied amount).
/// </summary>
public static class CurrencyConverter
{
    /// <summary>Bosnia's currency-board peg (fixed, not a market rate) - 1 EUR = 1.95583 KM.</summary>
    public const decimal EurToKmRate = 1.95583m;

    public static decimal ConvertKmToEur(decimal amountKm) =>
        Math.Round(amountKm / EurToKmRate, 2, MidpointRounding.AwayFromZero);
}
