using ClinicNow.Model.Exceptions;
using ClinicNow.Model.Requests;
using ClinicNow.Services.Database;
using ClinicNow.Services.Documents;
using ClinicNow.Services.News;
using ClinicNow.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace ClinicNow.Tests.News;

/// <summary>
/// Review item C17: <c>NewsItemService.DecodeImage</c> used to store whatever
/// <c>ImageContentType</c> the client sent - no whitelist, no signature check -
/// and defaulted a blank one to "image/png". <c>GetImage</c> then served those
/// bytes back under that unverified type. It now goes through
/// <see cref="FileValidation"/>, the same check patient documents use.
///
/// News accepts a deliberately narrower set than a patient document does:
/// pictures only (no PDF) and 5 MB rather than 10, because a PDF "image" would
/// never render in either client's image widget.
/// </summary>
public class NewsItemServiceTests
{
    // The genuine 1x1 PNG NewsItemConfiguration seeds, so a real signature check
    // is exercised rather than a placeholder.
    private const string ValidPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

    // A real minimal PDF - valid for a patient document, and exactly what news
    // must now refuse.
    private const string ValidPdfBase64 =
        "JVBERi0xLjQKMSAwIG9iajw8L1R5cGUvQ2F0YWxvZy9QYWdlcyAyIDAgUj4+ZW5kb2JqCjIgMCBvYmo8PC9UeXBlL1BhZ2VzL0tpZHNbMyAwIFJdL0NvdW50IDE+PmVuZG9iagozIDAgb2JqPDwvVHlwZS9QYWdlL1BhcmVudCAyIDAgUi9NZWRpYUJveFswIDAgMjAwIDIwMF0+PmVuZG9iagp4cmVmCjAgNAowMDAwMDAwMDAwIDY1NTM1IGYgCnRyYWlsZXI8PC9TaXplIDQvUm9vdCAxIDAgUj4+CnN0YXJ0eHJlZgowCiUlRU9G";

    private static NewsItemService NewService(ClinicNowContext context) =>
        new(context, TestContextFactory.CreateMapper());

    private static NewsItemInsertRequest Insert(string? base64, string? contentType) => new()
    {
        Title = "Nova obavijest",
        Text = "Tekst obavijesti.",
        ImageBase64 = base64,
        ImageContentType = contentType
    };

    [Fact]
    public async Task InsertAsync_ValidPng_StoresBytesAndDeclaredType()
    {
        await using var context = TestContextFactory.CreateContext();
        var service = NewService(context);

        var dto = await service.InsertAsync(Insert(ValidPngBase64, "image/png"));

        var entity = await context.NewsItems.SingleAsync(n => n.Id == dto.Id);
        Assert.Equal("image/png", entity.ImageContentType);
        Assert.Equal(Convert.FromBase64String(ValidPngBase64), entity.ImageData);
    }

    [Fact]
    public async Task InsertAsync_NoImage_SucceedsWithNullImage()
    {
        await using var context = TestContextFactory.CreateContext();
        var service = NewService(context);

        var dto = await service.InsertAsync(Insert(base64: null, contentType: null));

        var entity = await context.NewsItems.SingleAsync(n => n.Id == dto.Id);
        Assert.Null(entity.ImageData);
        Assert.Null(entity.ImageContentType);
    }

    /// <summary>
    /// The narrowing that matters: the shared helper accepts PDF for patient
    /// documents, but a news image must not inherit that.
    /// </summary>
    [Fact]
    public async Task InsertAsync_PdfRejectedEvenThoughDocumentsAllowIt()
    {
        await using var context = TestContextFactory.CreateContext();
        var service = NewService(context);

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.InsertAsync(Insert(ValidPdfBase64, "application/pdf")));

        Assert.True(error.Errors.ContainsKey("imageContentType"));
        Assert.False(await context.NewsItems.AnyAsync(n => n.Title == "Nova obavijest"));

        // Same bytes, same helper, document rules - still accepted, so the
        // narrowing is scoped to news and did not regress C4's callers.
        Assert.Equal(
            Convert.FromBase64String(ValidPdfBase64),
            FileValidation.DecodeAndValidateFile(ValidPdfBase64, "application/pdf"));
    }

    /// <summary>The actual attack C17 names: a non-image labelled as one.</summary>
    [Fact]
    public async Task InsertAsync_ContentTypeLyingAboutTheBytes_Rejected()
    {
        await using var context = TestContextFactory.CreateContext();
        var service = NewService(context);
        var notAPng = Convert.ToBase64String("MZ this is an executable"u8.ToArray());

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.InsertAsync(Insert(notAPng, "image/png")));

        Assert.True(error.Errors.ContainsKey("imageBase64"));
    }

    /// <summary>
    /// Previously a blank content type silently became "image/png". Guessing is
    /// exactly the unverified labelling this item removes.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("application/octet-stream")]
    public async Task InsertAsync_MissingOrUnsupportedContentType_Rejected(string? contentType)
    {
        await using var context = TestContextFactory.CreateContext();
        var service = NewService(context);

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.InsertAsync(Insert(ValidPngBase64, contentType)));

        Assert.True(error.Errors.ContainsKey("imageContentType"));
    }

    [Fact]
    public async Task InsertAsync_OversizedImage_Rejected()
    {
        await using var context = TestContextFactory.CreateContext();
        var service = NewService(context);

        // A real PNG header followed by enough padding to pass 5 MB, so the size
        // rule is what rejects it - not the signature check.
        var oversized = new byte[FileValidation.MaxImageBytes + 1];
        Convert.FromBase64String(ValidPngBase64).CopyTo(oversized, 0);

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.InsertAsync(Insert(Convert.ToBase64String(oversized), "image/png")));

        Assert.True(error.Errors.ContainsKey("imageBase64"));
    }

    /// <summary>The update path decodes through the same helper, not a copy.</summary>
    [Fact]
    public async Task UpdateAsync_InvalidReplacementImage_RejectedAndLeavesExistingImage()
    {
        await using var context = TestContextFactory.CreateContext();
        var service = NewService(context);
        var seededImage = await context.NewsItems.AsNoTracking().SingleAsync(n => n.Id == 1);

        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateAsync(1, new NewsItemUpdateRequest
        {
            Title = "Izmijenjena obavijest",
            Text = "Izmijenjeni tekst.",
            ImageBase64 = Convert.ToBase64String("not an image at all"u8.ToArray()),
            ImageContentType = "image/jpeg"
        }));

        var reloaded = await context.NewsItems.AsNoTracking().SingleAsync(n => n.Id == 1);
        Assert.Equal(seededImage.ImageData, reloaded.ImageData);
        Assert.Equal(seededImage.ImageContentType, reloaded.ImageContentType);
    }
}
