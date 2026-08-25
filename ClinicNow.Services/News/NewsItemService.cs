using ClinicNow.Model.Dto;
using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using MapsterMapper;

namespace ClinicNow.Services.News;

/// <summary>
/// Plain generic CRUD (no behaviour beyond validation + image decoding) - news
/// items don't need a bespoke service interface (rulebook Part II §D: reuse the
/// generic base classes wherever no extra behaviour is needed).
/// </summary>
public class NewsItemService : BaseCRUDService<NewsItemDto, NewsItemSearchObject, NewsItem, NewsItemInsertRequest, NewsItemUpdateRequest>
{
    // 5 MB - generous for a small announcement image while still bounding request size.
    private const int MaxImageBytes = 5 * 1024 * 1024;

    public NewsItemService(ClinicNowContext context, IMapper mapper) : base(context, mapper)
    {
    }

    protected override IQueryable<NewsItem> ApplyFilter(NewsItemSearchObject search, IQueryable<NewsItem> query)
    {
        if (!string.IsNullOrWhiteSpace(search.Title))
        {
            query = query.Where(n => n.Title.Contains(search.Title));
        }

        return query.OrderByDescending(n => n.CreatedAtUtc);
    }

    protected override Task BeforeInsertAsync(NewsItemInsertRequest request, NewsItem entity, CancellationToken cancellationToken)
    {
        ValidateText(request.Title, request.Text);

        entity.Title = request.Title.Trim();
        entity.Text = request.Text.Trim();
        entity.CreatedAtUtc = DateTime.UtcNow;
        (entity.ImageData, entity.ImageContentType) = DecodeImage(request.ImageBase64, request.ImageContentType);

        return Task.CompletedTask;
    }

    protected override Task BeforeUpdateAsync(NewsItemUpdateRequest request, NewsItem entity, CancellationToken cancellationToken)
    {
        ValidateText(request.Title, request.Text);

        entity.Title = request.Title.Trim();
        entity.Text = request.Text.Trim();

        if (!string.IsNullOrWhiteSpace(request.ImageBase64))
        {
            (entity.ImageData, entity.ImageContentType) = DecodeImage(request.ImageBase64, request.ImageContentType);
        }
        else if (request.RemoveImage)
        {
            entity.ImageData = null;
            entity.ImageContentType = null;
        }

        return Task.CompletedTask;
    }

    private static void ValidateText(string title, string text)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(title))
        {
            errors["title"] = ["Naslov je obavezan."];
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            errors["text"] = ["Tekst obavijesti je obavezan."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    private static (byte[]? Data, string? ContentType) DecodeImage(string? base64, string? contentType)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return (null, null);
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            throw new ValidationException("imageBase64", "Slika nije ispravno Base64 kodirana.");
        }

        if (bytes.Length > MaxImageBytes)
        {
            throw new ValidationException("imageBase64", "Slika je prevelika (maksimalno 5 MB).");
        }

        return (bytes, string.IsNullOrWhiteSpace(contentType) ? "image/png" : contentType);
    }
}
