using System.Security.Cryptography;

namespace ClinicNow.Services.Documents;

/// <summary>
/// Computes the stable identifier a stored file is served under as an HTTP ETag.
///
/// Used at write time only - every entity that holds bytes persists the result
/// alongside them. Hashing on read instead would mean loading the blob to decide
/// whether the blob needs sending, which defeats the point.
/// </summary>
public static class ContentHash
{
    /// <summary>
    /// SHA-256, hex, truncated to 32 characters (128 bits). Far past any collision
    /// concern for cache validation, and short enough to keep the header small.
    /// </summary>
    public static string Compute(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content))[..32];
}
