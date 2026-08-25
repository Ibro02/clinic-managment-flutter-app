namespace ClinicNow.Model.Dto;

/// <summary>
/// List/detail shape for a <c>NewsItem</c>. Never carries the raw image bytes
/// (rulebook Part II §D: list DTOs exclude heavy blobs) - only <see cref="HasImage"/>
/// and a convenience <see cref="ImageUrl"/> pointing at the dedicated image endpoint.
/// </summary>
public class NewsItemDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public bool HasImage { get; set; }
    public string? ImageUrl { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
