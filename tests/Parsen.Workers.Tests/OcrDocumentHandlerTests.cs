using Microsoft.Extensions.Time.Testing;
using Parsen.Application.Pipeline;
using Parsen.Domain.Dossiers;
using Parsen.Domain.Tenants;
using Parsen.Infrastructure.Documents;

namespace Parsen.Workers.Tests;

public sealed class OcrDocumentHandlerTests
{
    private static readonly TenantId Tenant = TenantId.From("ten_teste");

    private readonly InMemoryDocumentRepository _documents = new();
    private readonly FakeDocumentFiles _files = new();
    private readonly FakeOcr _ocr = new();
    private readonly OcrDocumentHandler _handler;

    public OcrDocumentHandlerTests() => _handler = new OcrDocumentHandler(_documents, _files, _ocr);

    [Fact]
    public async Task RecognizesEveryPageInOrderAndSavesWithTheOriginalRetention()
    {
        var document = await AddDocumentAsync();
        _files.RetentionDays = 30;

        var result = await _handler.HandleAsync(Tenant, document.DossierId, document.Id, pageCount: 3, CancellationToken.None);

        Assert.Equal([1, 2, 3], _files.ReadPages);
        Assert.Equal([1, 2, 3], result.Pages.Select(p => p.Page));
        Assert.Equal(30, _files.SavedOcr?.RetentionDays);
        Assert.Same(result, _files.SavedOcr?.Ocr);
    }

    [Fact]
    public async Task WithoutRetentionTag_UsesTheDefault90Days()
    {
        var document = await AddDocumentAsync();
        _files.RetentionDays = null;

        await _handler.HandleAsync(Tenant, document.DossierId, document.Id, pageCount: 1, CancellationToken.None);

        Assert.Equal(90, _files.SavedOcr?.RetentionDays);
    }

    [Fact]
    public async Task ZeroPages_IsAProgrammingError()
    {
        var document = await AddDocumentAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _handler.HandleAsync(Tenant, document.DossierId, document.Id, pageCount: 0, CancellationToken.None));
    }

    [Fact]
    public async Task UnknownDocument_IsATechnicalError()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(Tenant, DossierId.New(), DocumentId.New(), pageCount: 1, CancellationToken.None));
    }

    private async Task<Document> AddDocumentAsync()
    {
        var document = Document.Create(Tenant, DossierId.New(), DocumentContentType.Pdf, 1_000, new FakeTimeProvider());
        await _documents.AddAsync(document, CancellationToken.None);
        return document;
    }

    private sealed class FakeOcr : IOcrEngine
    {
        public Task<OcrPage> RecognizeAsync(byte[] png, int pageNumber, CancellationToken cancellationToken) =>
            Task.FromResult(new OcrPage(pageNumber, [new OcrLine($"linha da página {pageNumber}", 99.5, [0.1, 0.1, 0.5, 0.02], [])]));
    }
}
