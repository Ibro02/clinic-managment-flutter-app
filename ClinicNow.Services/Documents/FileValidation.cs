using ClinicNow.Model.Exceptions;

namespace ClinicNow.Services.Documents;

/// <summary>
/// Base64-decodes an uploaded file and validates it against both its declared
/// MIME type and its actual magic bytes (rulebook Part II §F: "MIME + magic
/// bytes, ne samo ekstenzija" - the declared Content-Type alone is never
/// trusted). Extracted out of <see cref="MedicalDocumentService"/> so
/// <c>LabFindingService</c> (review item C4) reuses the exact same check
/// instead of a second implementation - PLAN.md §5 is explicit about this.
/// </summary>
public static class FileValidation
{
    // 10 MB - generous for a scanned finding/report while still bounding request size.
    public const int MaxFileBytes = 10 * 1024 * 1024;

    // Every MIME type this validation accepts, and the magic bytes a genuine
    // file of that type must start with.
    private static readonly Dictionary<string, byte[][]> AllowedSignatures = new()
    {
        ["application/pdf"] = [[0x25, 0x50, 0x44, 0x46]], // %PDF
        ["image/png"] = [[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]],
        ["image/jpeg"] = [[0xFF, 0xD8, 0xFF]],
    };

    public static byte[] DecodeAndValidateFile(string base64, string declaredContentType)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            throw new ValidationException("fileBase64", "Fajl je obavezan.");
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            throw new ValidationException("fileBase64", "Fajl nije ispravno Base64 kodiran.");
        }

        if (bytes.Length == 0)
        {
            throw new ValidationException("fileBase64", "Fajl je prazan.");
        }

        if (bytes.Length > MaxFileBytes)
        {
            throw new ValidationException("fileBase64", "Fajl je prevelik (maksimalno 10 MB).");
        }

        if (!AllowedSignatures.TryGetValue(declaredContentType, out var signatures))
        {
            throw new ValidationException("contentType", "Dozvoljeni su samo PDF, PNG i JPEG fajlovi.");
        }

        // The declared Content-Type alone proves nothing - a renamed .exe with a
        // "application/pdf" header would sail through that check alone. Confirm
        // the file's actual leading bytes match a real file of that type.
        var matchesSignature = signatures.Any(signature =>
            bytes.Length >= signature.Length && bytes.Take(signature.Length).SequenceEqual(signature));

        if (!matchesSignature)
        {
            throw new ValidationException("fileBase64", "Sadržaj fajla ne odgovara prijavljenom tipu (MIME provjera nije prošla).");
        }

        return bytes;
    }
}
