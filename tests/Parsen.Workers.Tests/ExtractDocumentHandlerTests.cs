using Microsoft.Extensions.Time.Testing;
using Parsen.Application.Pipeline;
using Parsen.Domain.Dossiers;
using Parsen.Domain.Tenants;
using Parsen.Infrastructure.Documents;

namespace Parsen.Workers.Tests;

public sealed class ExtractDocumentHandlerTests
{
    private static readonly TenantId Tenant = TenantId.From("ten_teste");
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryDocumentRepository _documents = new();
    private readonly FakeDocumentFiles _files = new();
    private readonly FakeExtractionEngine _engine = new();
    private readonly FakeExtractionRepository _extractions = new();
    private readonly ExtractDocumentHandler _handler;

    public ExtractDocumentHandlerTests()
    {
        _files.StoredOcr = new OcrDocument(
        [
            new OcrPage(1,
            [
                new OcrLine("Líquido a receber", 99.0, [0.1, 0.8, 0.2, 0.02], [new OcrWord("Líquido", 99.0, [0.1, 0.8, 0.08, 0.02]), new OcrWord("a", 99.0, [0.19, 0.8, 0.01, 0.02]), new OcrWord("receber", 99.0, [0.21, 0.8, 0.09, 0.02])]),
                new OcrLine("4.870,35", 93.2, [0.6, 0.8, 0.1, 0.02], [new OcrWord("4.870,35", 93.2, [0.6, 0.8, 0.1, 0.02])]),
                new OcrLine("INSS 7,50% 365,00", 98.0, [0.1, 0.5, 0.6, 0.02], [new OcrWord("INSS", 99.0, [0.1, 0.5, 0.05, 0.02]), new OcrWord("7,50%", 98.0, [0.4, 0.5, 0.05, 0.02]), new OcrWord("365,00", 96.6, [0.6, 0.5, 0.1, 0.02])]),
            ]),
        ]);

        _handler = new ExtractDocumentHandler(_documents, _files, _engine, _extractions, new FakeTimeProvider(Now));
    }

    [Fact]
    public async Task FieldsGetEvidenceAndConfidenceFromTheOcr()
    {
        var document = await AddDocumentAsync();

        var result = await _handler.HandleAsync(Tenant, document.DossierId, document.Id, CancellationToken.None);

        var liquido = Assert.Single(result.Fields, f => f.Name == "salario_liquido");
        Assert.Equal(FieldValue.Number("4870.35"), liquido.Value);
        Assert.Equal([0.6, 0.8, 0.1, 0.02], liquido.Evidence!.Bbox!);
        Assert.Equal(0.93, liquido.Confidence);
    }

    [Fact]
    public async Task FieldAbsentFromTheDocument_StaysNullWithoutEvidence()
    {
        var document = await AddDocumentAsync();

        var result = await _handler.HandleAsync(Tenant, document.DossierId, document.Id, CancellationToken.None);

        Assert.Equal(new ExtractedField("cpf", null, null, null), Assert.Single(result.Fields, f => f.Name == "cpf"));
    }

    [Fact]
    public async Task SnippetTheModelInvented_HasZeroConfidence()
    {
        var document = await AddDocumentAsync();

        var result = await _handler.HandleAsync(Tenant, document.DossierId, document.Id, CancellationToken.None);

        var nome = Assert.Single(result.Fields, f => f.Name == "nome_funcionario");
        Assert.Equal(0, nome.Confidence);
        Assert.Null(nome.Evidence!.Bbox);
    }

    [Fact]
    public async Task ListItemsGetEvidenceToo()
    {
        var document = await AddDocumentAsync();

        var result = await _handler.HandleAsync(Tenant, document.DossierId, document.Id, CancellationToken.None);

        var inss = Assert.Single(result.Lists["itens"]);
        Assert.Equal(FieldValue.Text("desconto"), inss.Attributes["tipo"]);
        Assert.Equal(0.97, inss.Confidence);
    }

    [Fact]
    public async Task SavesTheResultWithVersionsAlertsAndTime()
    {
        var document = await AddDocumentAsync();

        var result = await _handler.HandleAsync(Tenant, document.DossierId, document.Id, CancellationToken.None);

        var (savedDocument, saved) = Assert.Single(_extractions.Saved);
        Assert.Equal(document.Id, savedDocument.Id);
        Assert.Same(result, saved);
        Assert.Equal("holerite", saved.DocumentType);
        Assert.Equal("holerite.v2", saved.SchemaId);
        Assert.Equal("modelo-de-teste", saved.ModelId);
        Assert.Equal(["CPF não aparece no documento"], saved.Alerts);
        Assert.Equal(Now, saved.ExtractedAt);
        Assert.Equal(new ModelUsage(4398, 1036, 0, 0), saved.Usage);
        Assert.Equal("holerite", _engine.RequestedType);
    }

    [Fact]
    public async Task UnknownDocument_IsATechnicalError()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.HandleAsync(Tenant, DossierId.New(), DocumentId.New(), CancellationToken.None));
        Assert.Empty(_extractions.Saved);
    }

    private async Task<Document> AddDocumentAsync()
    {
        var document = Document.Create(Tenant, DossierId.New(), DocumentContentType.Pdf, 1_000, new FakeTimeProvider());
        await _documents.AddAsync(document, CancellationToken.None);
        return document;
    }

    private sealed class FakeExtractionEngine : IExtractionEngine
    {
        public string? RequestedType { get; private set; }

        public Task<ModelExtraction> ExtractAsync(string documentType, OcrDocument ocr, CancellationToken cancellationToken)
        {
            RequestedType = documentType;
            return Task.FromResult(new ModelExtraction(
                "holerite.v2",
                "modelo-de-teste",
                new Dictionary<string, ModelField?>
                {
                    ["nome_funcionario"] = new(FieldValue.Text("FULANO DE TAL"), 1, "FULANO DE TAL"),
                    ["cpf"] = null,
                    ["salario_liquido"] = new(FieldValue.Number("4870.35"), 1, "4.870,35"),
                },
                new Dictionary<string, IReadOnlyList<ModelItem>>
                {
                    ["itens"] =
                    [
                        new(new Dictionary<string, FieldValue?>
                        {
                            ["codigo"] = null,
                            ["descricao"] = FieldValue.Text("INSS"),
                            ["tipo"] = FieldValue.Text("desconto"),
                            ["valor"] = FieldValue.Number("365.00"),
                        }, 1, "INSS 7,50% 365,00"),
                    ],
                },
                ["CPF não aparece no documento"],
                new ModelUsage(4398, 1036, 0, 0)));
        }
    }

    private sealed class FakeExtractionRepository : IExtractionRepository
    {
        public List<(Document Document, ExtractionResult Result)> Saved { get; } = [];

        public Task SaveAsync(Document document, ExtractionResult result, CancellationToken cancellationToken)
        {
            Saved.Add((document, result));
            return Task.CompletedTask;
        }
    }
}
