namespace ClinicNow.Model.Requests;

public class NewsItemInsertRequest
{
    public string Title { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;

    /// <summary>Base64-encoded image bytes, optional. Validated for a plausible size/format server-side.</summary>
    public string? ImageBase64 { get; set; }
    public string? ImageContentType { get; set; }
}

public class NewsItemUpdateRequest
{
    public string Title { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string? ImageBase64 { get; set; }
    public string? ImageContentType { get; set; }

    /// <summary>When true and no new image is supplied, removes the existing image.</summary>
    public bool RemoveImage { get; set; }
}
