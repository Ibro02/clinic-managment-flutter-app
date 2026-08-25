namespace ClinicNow.Services.Database.Entities;

/// <summary>
/// A clinic-wide news/announcement item (title, text, optional image, datetime -
/// rulebook Part II §G). The image is stored as raw bytes directly in the row
/// (no separate blob storage yet - proper file-upload infrastructure with MIME/
/// magic-byte validation is Phase 6's "Medical Documentation" scope); list
/// responses never include <see cref="ImageData"/> itself (rulebook Part II §D:
/// list DTOs exclude heavy blobs) - only a <c>HasImage</c> flag, with the actual
/// bytes served from a dedicated <c>GET /api/NewsItem/{id}/image</c> endpoint.
/// </summary>
public class NewsItem
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;

    public byte[]? ImageData { get; set; }
    public string? ImageContentType { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
