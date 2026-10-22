using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Time.Testing;
using Parsen.Domain.Dossiers;
using Parsen.Domain.Tenants;
using Parsen.Infrastructure.DynamoDb;

namespace Parsen.Infrastructure.Tests.DynamoDb;

public sealed class DynamoDbDocumentRepositoryTests(DynamoDbFixture fixture) : IClassFixture<DynamoDbFixture>
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 18, 14, 0, 0, TimeSpan.Zero).AddTicks(1234567);

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly DynamoDbDocumentRepository _repository = new(fixture.Client, fixture.Settings);

    private static TenantId NewTenant() => TenantId.From($"ten_{Guid.NewGuid():N}");

    [Fact]
    public async Task AddThenList_ReturnsDocumentsOfTheDossierInOrder()
    {
        var tenant = NewTenant();
        var dossierId = DossierId.New();
        var first = Document.Create(tenant, dossierId, DocumentContentType.Pdf, 53_979, _clock);

        // O ULID só ordena entre milissegundos diferentes; no mesmo milissegundo, a parte final é aleatória
        Thread.Sleep(2);
        var second = Document.Create(tenant, dossierId, DocumentContentType.Png, 1_024, _clock);

        await _repository.AddAsync(first, CancellationToken.None);
        await _repository.AddAsync(second, CancellationToken.None);
        var listed = await _repository.ListByDossierAsync(tenant, dossierId, CancellationToken.None);

        Assert.Equal([first.Id, second.Id], listed.Select(d => d.Id));
        var loaded = listed[0];
        Assert.Equal(tenant, loaded.TenantId);
        Assert.Equal(dossierId, loaded.DossierId);
        Assert.Equal(DocumentContentType.Pdf, loaded.ContentType);
        Assert.Equal(53_979, loaded.DeclaredSizeBytes);
        Assert.Equal(Now, loaded.CreatedAt);
    }

    [Fact]
    public async Task List_OtherTenantOrOtherDossier_ReturnsEmpty()
    {
        var tenant = NewTenant();
        var dossierId = DossierId.New();
        await _repository.AddAsync(Document.Create(tenant, dossierId, DocumentContentType.Pdf, 100, _clock), CancellationToken.None);

        Assert.Empty(await _repository.ListByDossierAsync(NewTenant(), dossierId, CancellationToken.None));
        Assert.Empty(await _repository.ListByDossierAsync(tenant, DossierId.New(), CancellationToken.None));
    }

    [Fact]
    public async Task Get_ReturnsTheDocumentOnlyForItsTenantAndDossier()
    {
        var tenant = NewTenant();
        var dossierId = DossierId.New();
        var document = Document.Create(tenant, dossierId, DocumentContentType.Pdf, 777, _clock);
        await _repository.AddAsync(document, CancellationToken.None);

        var loaded = await _repository.GetAsync(tenant, dossierId, document.Id, CancellationToken.None);

        Assert.Equal(document.Id, loaded?.Id);
        Assert.Equal(777, loaded?.DeclaredSizeBytes);
        Assert.Null(await _repository.GetAsync(NewTenant(), dossierId, document.Id, CancellationToken.None));
        Assert.Null(await _repository.GetAsync(tenant, DossierId.New(), document.Id, CancellationToken.None));
        Assert.Null(await _repository.GetAsync(tenant, dossierId, DocumentId.New(), CancellationToken.None));
    }

    [Fact]
    public async Task StoredItem_LivesInTheDossierPartition()
    {
        var tenant = TenantId.From("ten_doc_layout");
        var dossierId = DossierId.New();
        var document = Document.Create(tenant, dossierId, DocumentContentType.Jpeg, 2_048, _clock);
        await _repository.AddAsync(document, CancellationToken.None);

        var response = await fixture.Client.GetItemAsync(new GetItemRequest
        {
            TableName = fixture.Settings.TableName,
            Key = new()
            {
                ["PK"] = new AttributeValue($"TENANT#ten_doc_layout#DOSSIER#{dossierId}"),
                ["SK"] = new AttributeValue($"DOC#{document.Id}"),
            },
        });

        var item = response.Item;
        Assert.Equal("Document", item["EntityType"].S);
        Assert.Equal("image/jpeg", item["ContentType"].S);
        Assert.Equal("2048", item["DeclaredSizeBytes"].N);
        Assert.Equal("pending", item["Status"].S);
        Assert.False(item.ContainsKey("GSI1PK"));
    }

    [Fact]
    public async Task SaveClassification_SingleDocument_KeepsTypeAndPages()
    {
        var tenant = NewTenant();
        var document = Document.Create(tenant, DossierId.New(), DocumentContentType.Pdf, 1_000, _clock);
        await _repository.AddAsync(document, CancellationToken.None);

        document.Classify(DocumentType.Holerite, new PageRange(1, 2));
        await _repository.SaveClassificationAsync(document, [], CancellationToken.None);
        var loaded = await _repository.GetAsync(tenant, document.DossierId, document.Id, CancellationToken.None);

        Assert.Equal(DocumentStatus.Classified, loaded?.Status);
        Assert.Equal(DocumentType.Holerite, loaded?.Type);
        Assert.Equal(new PageRange(1, 2), loaded?.Pages);
        Assert.Null(loaded?.SourceDocumentId);
    }

    [Fact]
    public async Task SaveClassification_Split_StoresSourceAndChildrenTogether()
    {
        var tenant = NewTenant();
        var source = Document.Create(tenant, DossierId.New(), DocumentContentType.Pdf, 1_000, _clock);
        await _repository.AddAsync(source, CancellationToken.None);

        var children = source.Split(
            [(DocumentType.Holerite, new PageRange(1, 1)), (DocumentType.ExtratoBancario, new PageRange(2, 4))], _clock);
        await _repository.SaveClassificationAsync(source, children, CancellationToken.None);
        var listed = await _repository.ListByDossierAsync(tenant, source.DossierId, CancellationToken.None);

        Assert.Equal(3, listed.Count);
        Assert.Equal(DocumentStatus.Split, listed.Single(d => d.Id == source.Id).Status);
        var extrato = listed.Single(d => d.Type == DocumentType.ExtratoBancario);
        Assert.Equal(source.Id, extrato.SourceDocumentId);
        Assert.Equal(source.Id, extrato.FileId);
        Assert.Equal(new PageRange(2, 4), extrato.Pages);
        Assert.Equal(DocumentStatus.Classified, extrato.Status);
    }

    [Fact]
    public async Task Update_PersistsTheNewStatus()
    {
        var tenant = NewTenant();
        var document = Document.Create(tenant, DossierId.New(), DocumentContentType.Pdf, 1_000, _clock);
        await _repository.AddAsync(document, CancellationToken.None);
        document.Classify(DocumentType.Holerite, new PageRange(1, 1));
        await _repository.SaveClassificationAsync(document, [], CancellationToken.None);

        document.MarkExtracted();
        await _repository.UpdateAsync(document, CancellationToken.None);

        var loaded = await _repository.GetAsync(tenant, document.DossierId, document.Id, CancellationToken.None);
        Assert.Equal(DocumentStatus.Extracted, loaded?.Status);
    }

    [Fact]
    public async Task Update_OfADocumentThatDoesNotExist_Fails()
    {
        var document = Document.Create(NewTenant(), DossierId.New(), DocumentContentType.Pdf, 1_000, _clock);

        await Assert.ThrowsAsync<ConditionalCheckFailedException>(() => _repository.UpdateAsync(document, CancellationToken.None));
    }

    // Documentos gravados na Fase 1 não têm Status: são lidos como pendentes
    [Fact]
    public async Task ItemWithoutStatus_IsReadAsPending()
    {
        var tenant = NewTenant();
        var dossierId = DossierId.New();
        var documentId = DocumentId.New();

        // Item exatamente como a Fase 1 gravava
        await fixture.Client.PutItemAsync(new PutItemRequest
        {
            TableName = fixture.Settings.TableName,
            Item = new()
            {
                ["PK"] = new AttributeValue($"TENANT#{tenant}#DOSSIER#{dossierId}"),
                ["SK"] = new AttributeValue($"DOC#{documentId}"),
                ["EntityType"] = new AttributeValue("Document"),
                ["Id"] = new AttributeValue(documentId.ToString()),
                ["TenantId"] = new AttributeValue(tenant.Value),
                ["DossierId"] = new AttributeValue(dossierId.ToString()),
                ["ContentType"] = new AttributeValue("application/pdf"),
                ["DeclaredSizeBytes"] = new AttributeValue { N = "1000" },
                ["CreatedAt"] = new AttributeValue("2026-10-18T14:00:00.0000000Z"),
            },
        });

        var loaded = await _repository.GetAsync(tenant, dossierId, documentId, CancellationToken.None);

        Assert.Equal(DocumentStatus.Pending, loaded?.Status);
    }
}
