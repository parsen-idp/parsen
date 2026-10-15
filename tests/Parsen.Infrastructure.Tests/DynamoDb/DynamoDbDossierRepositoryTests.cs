using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Time.Testing;
using Parsen.Application.Common;
using Parsen.Domain.Dossiers;
using Parsen.Domain.Tenants;
using Parsen.Infrastructure.DynamoDb;

namespace Parsen.Infrastructure.Tests.DynamoDb;

public sealed class DynamoDbDossierRepositoryTests(DynamoDbFixture fixture) : IClassFixture<DynamoDbFixture>
{
    // Hora com frações de segundo, para provar que a data volta idêntica
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 15, 14, 53, 39, TimeSpan.Zero).AddTicks(5166173);

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly DynamoDbDossierRepository _repository = new(fixture.Client, fixture.Settings);

    // Cada teste usa um tenant próprio para não interferir nos outros
    private static TenantId NewTenant() => TenantId.From($"ten_{Guid.NewGuid():N}");

    [Fact]
    public async Task AddThenGet_ReturnsEquivalentDossier()
    {
        var dossier = Dossier.Create(NewTenant(), AnalysisType.Income, "proposta-42", _clock);

        await _repository.AddAsync(dossier, CancellationToken.None);
        var loaded = await _repository.GetAsync(dossier.TenantId, dossier.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(dossier.Id, loaded.Id);
        Assert.Equal(dossier.TenantId, loaded.TenantId);
        Assert.Equal(AnalysisType.Income, loaded.AnalysisType);
        Assert.Equal("proposta-42", loaded.ExternalRef);
        Assert.Equal(DossierStatus.Draft, loaded.Status);
        Assert.Equal(Now, loaded.CreatedAt);
        Assert.Equal(Now, loaded.UpdatedAt);
        Assert.Null(loaded.DecidedBy);
        Assert.Null(loaded.RejectionReason);
        Assert.Equal(1, loaded.Version);
    }

    [Fact]
    public async Task Get_FromOtherTenant_ReturnsNull()
    {
        var dossier = Dossier.Create(NewTenant(), AnalysisType.Income, null, _clock);
        await _repository.AddAsync(dossier, CancellationToken.None);

        var loaded = await _repository.GetAsync(NewTenant(), dossier.Id, CancellationToken.None);

        Assert.Null(loaded);
    }

    [Fact]
    public async Task Get_UnknownId_ReturnsNull()
    {
        Assert.Null(await _repository.GetAsync(NewTenant(), DossierId.New(), CancellationToken.None));
    }

    [Fact]
    public async Task Add_SameIdTwice_ThrowsConflictAndKeepsFirst()
    {
        var first = Dossier.Create(NewTenant(), AnalysisType.Income, "primeiro", _clock);
        var duplicate = Dossier.Restore(
            first.Id, first.TenantId, AnalysisType.Property, "segundo", DossierStatus.Draft,
            Now, Now, null, null, version: 1);
        await _repository.AddAsync(first, CancellationToken.None);

        await Assert.ThrowsAsync<PersistenceConflictException>(() => _repository.AddAsync(duplicate, CancellationToken.None));

        var loaded = await _repository.GetAsync(first.TenantId, first.Id, CancellationToken.None);
        Assert.Equal("primeiro", loaded?.ExternalRef);
    }

    [Fact]
    public async Task StoredItem_HasKeysAndStableAttributeValues()
    {
        var dossier = Dossier.Create(TenantId.From("ten_layout"), AnalysisType.Income, "proposta-42", _clock);
        await _repository.AddAsync(dossier, CancellationToken.None);

        var item = await GetRawItemAsync("TENANT#ten_layout", $"DOSSIER#{dossier.Id}");

        Assert.Equal("TENANT#ten_layout#STATUS#draft", item["GSI1PK"].S);
        Assert.Equal($"2026-10-15T14:53:39.5166173Z#{dossier.Id}", item["GSI1SK"].S);
        Assert.Equal("TENANT#ten_layout#REF#proposta-42", item["GSI2PK"].S);
        Assert.Equal($"DOSSIER#{dossier.Id}", item["GSI2SK"].S);
        Assert.Equal("Dossier", item["EntityType"].S);
        Assert.Equal("income", item["AnalysisType"].S);
        Assert.Equal("draft", item["Status"].S);
        Assert.Equal("2026-10-15T14:53:39.5166173Z", item["CreatedAt"].S);
        Assert.Equal("1", item["Version"].N);
    }

    [Fact]
    public async Task StoredItem_WithoutExternalRef_StaysOutOfGsi2()
    {
        var dossier = Dossier.Create(NewTenant(), AnalysisType.Income, null, _clock);
        await _repository.AddAsync(dossier, CancellationToken.None);

        var item = await GetRawItemAsync($"TENANT#{dossier.TenantId}", $"DOSSIER#{dossier.Id}");

        Assert.False(item.ContainsKey("GSI2PK"));
        Assert.False(item.ContainsKey("GSI2SK"));
        Assert.False(item.ContainsKey("ExternalRef"));
    }

    public static TheoryData<DossierStatus> AllStatuses => new(Enum.GetValues<DossierStatus>());

    [Theory]
    [MemberData(nameof(AllStatuses))]
    public async Task RoundTrip_PreservesEveryStatus(DossierStatus status)
    {
        var dossier = Dossier.Restore(
            DossierId.New(), NewTenant(), AnalysisType.Property, null, status,
            Now, Now.AddMinutes(5), "usr_analista", "motivo", version: 3);

        await _repository.AddAsync(dossier, CancellationToken.None);
        var loaded = await _repository.GetAsync(dossier.TenantId, dossier.Id, CancellationToken.None);

        Assert.Equal(status, loaded?.Status);
        Assert.Equal(3, loaded?.Version);
        Assert.Equal("usr_analista", loaded?.DecidedBy);
        Assert.Equal("motivo", loaded?.RejectionReason);
    }

    private async Task<Dictionary<string, AttributeValue>> GetRawItemAsync(string pk, string sk)
    {
        var response = await fixture.Client.GetItemAsync(new GetItemRequest
        {
            TableName = fixture.Settings.TableName,
            Key = new() { ["PK"] = new AttributeValue(pk), ["SK"] = new AttributeValue(sk) },
        });
        return response.Item;
    }
}
