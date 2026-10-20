using Microsoft.Extensions.Time.Testing;
using Parsen.Application.Pipeline;
using Parsen.Domain.Dossiers;
using Parsen.Domain.Tenants;
using Parsen.Infrastructure.Documents;

namespace Parsen.Workers.Tests;

public sealed class IngestDocumentHandlerTests
{
    private static readonly TenantId Tenant = TenantId.From("ten_teste");

    private readonly InMemoryDocumentRepository _documents = new();
    private readonly FakeDocumentFiles _files = new();
    private readonly FakeRenderer _renderer = new();
    private readonly IngestDocumentHandler _handler;

    public IngestDocumentHandlerTests() => _handler = new IngestDocumentHandler(_documents, _files, _renderer);

    [Fact]
    public async Task SavesEveryPageWithTheOriginalRetention()
    {
        var document = await AddDocumentAsync();
        _files.RetentionDays = 30;
        _renderer.Pages = 3;

        var pageCount = await _handler.HandleAsync(Tenant, document.DossierId, document.Id, CancellationToken.None);

        Assert.Equal(3, pageCount);
        Assert.Equal([1, 2, 3], _files.SavedPages.Select(s => s.PageNumber));
        Assert.All(_files.SavedPages, s => Assert.Equal(30, s.RetentionDays));
    }

    [Fact]
    public async Task WithoutRetentionTag_UsesTheDefault90Days()
    {
        var document = await AddDocumentAsync();
        _files.RetentionDays = null;

        await _handler.HandleAsync(Tenant, document.DossierId, document.Id, CancellationToken.None);

        Assert.All(_files.SavedPages, s => Assert.Equal(90, s.RetentionDays));
    }

    [Fact]
    public async Task TooManyPages_IsRejectedBeforeRendering()
    {
        var document = await AddDocumentAsync();
        _renderer.Pages = UploadLimits.MaxPagesPerDossier + 1;

        var ex = await Assert.ThrowsAsync<DocumentRejectedException>(
            () => _handler.HandleAsync(Tenant, document.DossierId, document.Id, CancellationToken.None));

        Assert.Equal("PAGINAS_EXCEDIDAS", ex.Code);
        Assert.False(_renderer.Rendered);
        Assert.Empty(_files.SavedPages);
    }

    [Fact]
    public async Task NoPages_IsRejected()
    {
        var document = await AddDocumentAsync();
        _renderer.Pages = 0;

        var ex = await Assert.ThrowsAsync<DocumentRejectedException>(
            () => _handler.HandleAsync(Tenant, document.DossierId, document.Id, CancellationToken.None));

        Assert.Equal("DOCUMENTO_SEM_PAGINAS", ex.Code);
    }

    [Fact]
    public async Task UnknownDocument_IsATechnicalError()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(Tenant, DossierId.New(), DocumentId.New(), CancellationToken.None));
    }

    private async Task<Document> AddDocumentAsync()
    {
        var document = Document.Create(Tenant, DossierId.New(), DocumentContentType.Pdf, 1_000, new FakeTimeProvider());
        await _documents.AddAsync(document, CancellationToken.None);
        return document;
    }

    private sealed class FakeRenderer : IPageRenderer
    {
        public int Pages { get; set; } = 2;

        public bool Rendered { get; private set; }

        public int CountPages(byte[] content, DocumentContentType contentType) => Pages;

        public IReadOnlyList<byte[]> Render(byte[] content, DocumentContentType contentType)
        {
            Rendered = true;
            return [.. Enumerable.Range(0, Pages).Select(_ => new byte[] { 0x89 })];
        }
    }
}
