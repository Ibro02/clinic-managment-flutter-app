using ClinicNow.Model.Dto;
using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using ClinicNow.Services.Documents;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;

namespace ClinicNow.Services.News;

/// <summary>
/// Generic CRUD (validation + image decoding) plus one bespoke read: serving the
/// stored image bytes, which stay off the list/detail DTO (rulebook Part II §D).
/// That read lives here rather than in the controller - fetching it there was
/// the layering violation review item C19 calls out.
/// </summary>
public class NewsItemService : BaseCRUDService<NewsItemDto, NewsItemSearchObject, NewsItem, NewsItemInsertRequest, NewsItemUpdateRequest>, INewsItemService
{
    public NewsItemService(ClinicNowContext context, IMapper mapper) : base(context, mapper)
    {
    }

    /// <inheritdoc />
    public async Task<(byte[] Data, string ContentType, string? ContentHash)?> GetImageAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await Context.NewsItems.AsNoTracking()
            .SingleOrDefaultAsync(n => n.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(NewsItem), id);

        return entity.ImageData is null || entity.ImageData.Length == 0
            ? null
            : (entity.ImageData, entity.ImageContentType ?? "application/octet-stream", entity.ContentHash);
    }

    /// <summary>
    /// Overrides the generic paged read purely to keep <c>ImageData</c> out of the
    /// SELECT. <see cref="NewsItemDto"/> already excludes the bytes, but the
    /// inherited implementation materializes whole <see cref="NewsItem"/> entities
    /// and maps afterwards - so a page of ten announcements read up to 50 MB of
    /// image data off the server only to throw it away. Projecting in the query
    /// means the blob is never read at all; clients fetch it from
    /// <c>/api/NewsItem/{id}/image</c> when they actually need to render it.
    /// </summary>
    public override async Task<ClinicNow.Model.Common.PagedResult<NewsItemDto>> GetPagedAsync(
        NewsItemSearchObject search, CancellationToken cancellationToken = default)
    {
        var query = ApplyFilter(search, Context.NewsItems.AsQueryable());

        var count = await query.CountAsync(cancellationToken);

        query = ApplySorting(search, query);

        var rows = await query
            .Skip((search.Page - 1) * search.PageSize)
            .Take(search.PageSize)
            .Select(n => new NewsItemDto
            {
                Id = n.Id,
                Title = n.Title,
                Text = n.Text,
                // A null test and nothing more. Adding `&& n.ImageData.Length > 0`
                // reads naturally but has no SQL translation for a varbinary column,
                // so EF silently fell back to selecting ImageData itself and
                // evaluating the length in memory - which put the entire blob back
                // into the very query this override exists to keep it out of.
                // Safe as the sole test because the write path only ever stores null
                // or validated non-empty bytes (FileValidation rejects an empty file).
                HasImage = n.ImageData != null,
                CreatedAtUtc = n.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        // Interpolation has no SQL translation, so the URL is filled in here - same
        // shape NotificationMappingConfig produces for the non-list paths.
        foreach (var row in rows)
        {
            row.ImageUrl = row.HasImage ? $"/api/NewsItem/{row.Id}/image" : null;
        }

        return new ClinicNow.Model.Common.PagedResult<NewsItemDto>
        {
            Count = count,
            ResultList = rows
        };
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
        entity.ContentHash = entity.ImageData is null ? null : Documents.ContentHash.Compute(entity.ImageData);

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
            entity.ContentHash = entity.ImageData is null ? null : Documents.ContentHash.Compute(entity.ImageData);
        }
        else if (request.RemoveImage)
        {
            entity.ImageData = null;
            entity.ImageContentType = null;
            entity.ContentHash = null;
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

    /// <summary>
    /// The image is optional - a news item may carry none, and an update that
    /// leaves it alone sends no base64 - so a blank value means "no image"
    /// rather than a validation failure. Anything actually supplied goes through
    /// the same MIME whitelist and magic-byte check patient documents use
    /// (review item C17): this method previously stored the client's declared
    /// content type unverified, defaulting a blank one to "image/png".
    /// </summary>
    private static (byte[]? Data, string? ContentType) DecodeImage(string? base64, string? contentType)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return (null, null);
        }

        return (FileValidation.DecodeAndValidateImage(base64, contentType), contentType);
    }
}
