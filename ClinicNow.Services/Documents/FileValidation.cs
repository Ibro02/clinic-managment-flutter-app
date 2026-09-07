using ClinicNow.Model.Exceptions;

namespace ClinicNow.Services.Documents;

/// <summary>
/// Base64-decodes an upload and validates it against both its declared MIME
/// type and its actual magic bytes (rulebook Part II §F: "MIME + magic bytes,
/// not just the extension" - the declared Content-Type alone is never trusted).
/// Extracted out of <see cref="MedicalDocumentService"/> so
/// <c>LabFindingService</c> (review item C4) reuses the exact same check
/// instead of a second implementation - PLAN.md §5 is explicit about this.
///
/// Two entry points share one signature table and one magic-byte check:
/// <see cref="DecodeAndValidateFile"/> for patient documents/lab findings, and
/// <see cref="DecodeAndValidateImage"/> for news images (review item C17),
/// which are pictures only - a PDF announcement "image" would never render in
/// the clients' image widgets, so news deliberately accepts a narrower set and
/// a smaller cap than a scanned medical report needs.
/// </summary>
public static class FileValidation
{
    // 10 MB - generous for a scanned finding/report while still bounding request size.
    public const int MaxFileBytes = 10 * 1024 * 1024;

    // 5 MB - generous for a small announcement image, and the limit news already enforced
    // before C17 gave it a whitelist.
    public const int MaxImageBytes = 5 * 1024 * 1024;

    // Every MIME type this validation knows, and the magic bytes a genuine file
    // of that type must start with. Which subset a caller accepts is decided by
    // its ValidationRules, but the signatures themselves live here only.
    private static readonly Dictionary<string, byte[][]> AllowedSignatures = new()
    {
        ["application/pdf"] = [[0x25, 0x50, 0x44, 0x46]], // %PDF
        ["image/png"] = [[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]],
        ["image/jpeg"] = [[0xFF, 0xD8, 0xFF]],
    };

    /// <summary>
    /// What one caller accepts and what it tells the user when it refuses.
    /// Field names matter: the clients render a validation message under the
    /// control it names (rulebook §4), and news posts an image as
    /// <c>imageBase64</c> where documents post a file as <c>fileBase64</c>.
    /// </summary>
    private sealed record ValidationRules(
        string[] AllowedContentTypes,
        int MaxBytes,
        string ContentField,
        string ContentTypeField,
        string RequiredMessage,
        string NotBase64Message,
        string EmptyMessage,
        string TooLargeMessage,
        string TypeNotAllowedMessage,
        string SignatureMismatchMessage);

    private static readonly ValidationRules DocumentRules = new(
        AllowedContentTypes: ["application/pdf", "image/png", "image/jpeg"],
        MaxBytes: MaxFileBytes,
        ContentField: "fileBase64",
        ContentTypeField: "contentType",
        RequiredMessage: "Fajl je obavezan.",
        NotBase64Message: "Fajl nije ispravno Base64 kodiran.",
        EmptyMessage: "Fajl je prazan.",
        TooLargeMessage: "Fajl je prevelik (maksimalno 10 MB).",
        TypeNotAllowedMessage: "Dozvoljeni su samo PDF, PNG i JPEG fajlovi.",
        SignatureMismatchMessage: "Sadržaj fajla ne odgovara prijavljenom tipu (MIME provjera nije prošla).");

    private static readonly ValidationRules ImageRules = new(
        AllowedContentTypes: ["image/png", "image/jpeg"],
        MaxBytes: MaxImageBytes,
        ContentField: "imageBase64",
        ContentTypeField: "imageContentType",
        RequiredMessage: "Slika je obavezna.",
        NotBase64Message: "Slika nije ispravno Base64 kodirana.",
        EmptyMessage: "Slika je prazna.",
        TooLargeMessage: "Slika je prevelika (maksimalno 5 MB).",
        TypeNotAllowedMessage: "Dozvoljene su samo PNG i JPEG slike.",
        SignatureMismatchMessage: "Sadržaj slike ne odgovara prijavljenom tipu (MIME provjera nije prošla).");

    /// <summary>Patient documents and lab findings: PDF, PNG or JPEG, up to 10 MB.</summary>
    public static byte[] DecodeAndValidateFile(string? base64, string? declaredContentType) =>
        DecodeAndValidate(base64, declaredContentType, DocumentRules);

    /// <summary>News images: PNG or JPEG only, up to 5 MB (review item C17).</summary>
    public static byte[] DecodeAndValidateImage(string? base64, string? declaredContentType) =>
        DecodeAndValidate(base64, declaredContentType, ImageRules);

    private static byte[] DecodeAndValidate(string? base64, string? declaredContentType, ValidationRules rules)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            throw new ValidationException(rules.ContentField, rules.RequiredMessage);
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            throw new ValidationException(rules.ContentField, rules.NotBase64Message);
        }

        if (bytes.Length == 0)
        {
            throw new ValidationException(rules.ContentField, rules.EmptyMessage);
        }

        if (bytes.Length > rules.MaxBytes)
        {
            throw new ValidationException(rules.ContentField, rules.TooLargeMessage);
        }

        // An unset Content-Type is a rejection, not a default: guessing one here
        // would hand the caller back exactly the unverified label C17 exists to
        // stop trusting.
        if (declaredContentType is null
            || !rules.AllowedContentTypes.Contains(declaredContentType)
            || !AllowedSignatures.TryGetValue(declaredContentType, out var signatures))
        {
            throw new ValidationException(rules.ContentTypeField, rules.TypeNotAllowedMessage);
        }

        // The declared Content-Type alone proves nothing - a renamed .exe with an
        // "image/png" header would sail through that check alone. Confirm the
        // file's actual leading bytes match a real file of that type.
        var matchesSignature = signatures.Any(signature =>
            bytes.Length >= signature.Length && bytes.Take(signature.Length).SequenceEqual(signature));

        if (!matchesSignature)
        {
            throw new ValidationException(rules.ContentField, rules.SignatureMismatchMessage);
        }

        return bytes;
    }
}
