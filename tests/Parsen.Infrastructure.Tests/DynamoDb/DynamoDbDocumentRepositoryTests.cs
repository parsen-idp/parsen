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
        Assert.False(item.ContainsKey("GSI1PK"));
    }
}
