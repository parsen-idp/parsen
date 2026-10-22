using Microsoft.Extensions.Time.Testing;
using Parsen.Application.Pipeline;
using Parsen.Domain.Dossiers;
using Parsen.Domain.Tenants;
using Parsen.Infrastructure.Documents;

namespace Parsen.Workers.Tests;

public sealed class ClassifyDocumentHandlerTests
{
    private static readonly TenantId Tenant = TenantId.From("ten_teste");

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryDocumentRepository _documents = new();
    private readonly FakeDocumentFiles _files = new();
    private readonly FakeClassifier _classifier = new();
    private readonly ClassifyDocumentHandler _handler;

    public ClassifyDocumentHandlerTests()
    {
        _files.StoredOcr = new OcrDocument([.. Enumerable.Range(1, 4).Select(p => new OcrPage(p, []))]);
        _handler = new ClassifyDocumentHandler(_documents, _files, _classifier, _clock);
    }

    [Fact]
    public async Task SingleDocument_ClassifiesTheUploadItself()
    {
        var file = await AddFileAsync();
        _classifier.Parts = [new(DocumentType.Holerite, new PageRange(1, 4))];

        var result = await _handler.HandleAsync(Tenant, file.DossierId, file.Id, CancellationToken.None);

        var document = Assert.Single(result.Documents);
        Assert.Equal(file.Id, document.Id);
        Assert.Equal(DocumentStatus.Classified, document.Status);
        Assert.Equal(DocumentType.Holerite, document.Type);
        Assert.False(result.Reused);
    }

    [Fact]
    public async Task SeveralDocuments_SplitIntoChildren()
    {
        var file = await AddFileAsync();
        _classifier.Parts = [new(DocumentType.Holerite, new PageRange(1, 1)), new(DocumentType.ExtratoBancario, new PageRange(2, 4))];

        var result = await _handler.HandleAsync(Tenant, file.DossierId, file.Id, CancellationToken.None);

        Assert.Equal(DocumentStatus.Split, file.Status);
        Assert.Equal([DocumentType.Holerite, DocumentType.ExtratoBancario], result.Documents.Select(d => d.Type!.Value));
        Assert.All(result.Documents, d => Assert.Equal(file.Id, d.SourceDocumentId));
        Assert.Equal(3, (await _documents.ListByDossierAsync(Tenant, file.DossierId, CancellationToken.None)).Count);
    }

    // Retry do Step Functions: a classificação já gravada é reaproveitada, sem nova chamada (paga) ao modelo
    [Fact]
    public async Task AlreadyClassified_ReturnsTheSameDocumentsWithoutCallingTheModel()
    {
        var file = await AddFileAsync();
        _classifier.Parts = [new(DocumentType.Holerite, new PageRange(1, 1)), new(DocumentType.ExtratoBancario, new PageRange(2, 4))];
        var first = await _handler.HandleAsync(Tenant, file.DossierId, file.Id, CancellationToken.None);

        var second = await _handler.HandleAsync(Tenant, file.DossierId, file.Id, CancellationToken.None);

        Assert.Equal(1, _classifier.Calls);
        Assert.True(second.Reused);
        Assert.Equal(first.Documents.Select(d => d.Id), second.Documents.Select(d => d.Id));
    }

    [Fact]
    public async Task UnorderedParts_AreSortedByPage()
    {
        var file = await AddFileAsync();
        _classifier.Parts = [new(DocumentType.ExtratoBancario, new PageRange(2, 4)), new(DocumentType.Holerite, new PageRange(1, 1))];

        var result = await _handler.HandleAsync(Tenant, file.DossierId, file.Id, CancellationToken.None);

        Assert.Equal([new PageRange(1, 1), new PageRange(2, 4)], result.Documents.Select(d => d.Pages!.Value));
    }

    // Resposta inválida não trava o dossiê: o arquivo inteiro vira "outro", com alerta, e vai para a revisão
    [Theory]
    [InlineData(1, 2, 2, 4)] // sobreposição
    [InlineData(1, 2, 3, 9)] // página que o arquivo não tem
    public async Task InvalidParts_FallBackToASingleUnknownDocument(int firstA, int lastA, int firstB, int lastB)
    {
        var file = await AddFileAsync();
        _classifier.Parts = [new(DocumentType.Holerite, new PageRange(firstA, lastA)), new(DocumentType.ExtratoBancario, new PageRange(firstB, lastB))];

        var result = await _handler.HandleAsync(Tenant, file.DossierId, file.Id, CancellationToken.None);

        var document = Assert.Single(result.Documents);
        Assert.Equal(DocumentType.Outro, document.Type);
        Assert.Equal(new PageRange(1, 4), document.Pages);
        Assert.Contains(result.Alerts, a => a.Contains("inválida", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnknownDocument_IsATechnicalError()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(Tenant, DossierId.New(), DocumentId.New(), CancellationToken.None));
    }

    private async Task<Document> AddFileAsync()
    {
        var document = Document.Create(Tenant, DossierId.New(), DocumentContentType.Pdf, 1_000, _clock);
        await _documents.AddAsync(document, CancellationToken.None);
        return document;
    }

    private sealed class FakeClassifier : IDocumentClassifier
    {
        public IReadOnlyList<ClassifiedPart> Parts { get; set; } = [];

        public int Calls { get; private set; }

        public Task<ClassificationOutput> ClassifyAsync(OcrDocument ocr, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new ClassificationOutput("classificacao.v1", "modelo-de-teste", Parts, [], new ModelUsage(1500, 150, 0, 0)));
        }
    }
}
