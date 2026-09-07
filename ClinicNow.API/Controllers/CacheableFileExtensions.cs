using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace ClinicNow.API.Controllers;

/// <summary>
/// Shared helper for the endpoints that serve stored bytes (patient documents, lab
/// findings, news images).
///
/// All three previously returned the full body on every single request. Their
/// content never changes once written - a new upload is a new row - so they are
/// ideal candidates for a validator, and the clients are a phone and a desktop app
/// that re-render the same lists repeatedly. Attaching the stored content hash as an
/// ETag lets the framework answer a repeat request with 304 and no body at all.
/// </summary>
public static class CacheableFileExtensions
{
    /// <summary>
    /// One day. These are private, per-patient files, so the cache directive is
    /// `private` - a shared proxy must never hold one - and the ETag still forces a
    /// revalidation once the window passes.
    /// </summary>
    private const int MaxAgeSeconds = 86_400;

    /// <summary>
    /// Returns the file with cache headers, degrading to a plain uncached response
    /// when <paramref name="contentHash"/> is null - which is the case for rows
    /// written before the hash column existed. Those simply keep the old behaviour
    /// rather than being served under a validator that cannot be trusted.
    /// </summary>
    public static IActionResult CacheableFile(
        this ControllerBase controller,
        byte[] content,
        string contentType,
        string? contentHash,
        string? fileName)
    {
        if (string.IsNullOrEmpty(contentHash))
        {
            return fileName is null
                ? controller.File(content, contentType)
                : controller.File(content, contentType, fileName);
        }

        controller.Response.Headers.CacheControl = $"private, max-age={MaxAgeSeconds}";

        // Strong ETag: the hash covers the exact bytes being returned.
        var entityTag = new EntityTagHeaderValue($"\"{contentHash}\"");

        return fileName is null
            ? controller.File(content, contentType, lastModified: null, entityTag: entityTag)
            : controller.File(content, contentType, fileName, lastModified: null, entityTag: entityTag);
    }
}
