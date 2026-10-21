using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Parsen.Application.Pipeline;
using Parsen.Domain.Dossiers;
using Parsen.Domain.Tenants;
using Parsen.Infrastructure.Dossiers;
using Parsen.Workers.Common;

namespace Parsen.Workers.Tests;

public sealed class FinalizeAndContractsTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryDossierRepository _dossiers = new();

    // ---------- Finalize ----------

    [Fact]
    public async Task MarkFailed_MovesProcessingDossierToFailed()
    {
        var dossier = await AddProcessingDossierAsync();

        await new FinalizeDossierHandler(_dossiers, _clock).MarkFailedAsync(dossier.TenantId, dossier.Id, CancellationToken.None);

        var loaded = await _dossiers.GetAsync(dossier.TenantId, dossier.Id, CancellationToken.None);
        Assert.Equal(DossierStatus.Failed, loaded?.Status);
    }

    [Fact]
    public async Task MarkFailed_IsIdempotent()
    {
        var dossier = await AddProcessingDossierAsync();
        var handler = new FinalizeDossierHandler(_dossiers, _clock);
        await handler.MarkFailedAsync(dossier.TenantId, dossier.Id, CancellationToken.None);
        var versionAfterFirst = dossier.Version;

        // O Step Functions pode repetir a etapa: a segunda chamada não muda nada nem falha
        await handler.MarkFailedAsync(dossier.TenantId, dossier.Id, CancellationToken.None);

        Assert.Equal(versionAfterFirst, dossier.Version);
    }

    [Fact]
    public async Task Complete_MovesProcessingDossierToNeedsReview()
    {
        var dossier = await AddProcessingDossierAsync();

        await new FinalizeDossierHandler(_dossiers, _clock).CompleteAsync(dossier.TenantId, dossier.Id, CancellationToken.None);

        var loaded = await _dossiers.GetAsync(dossier.TenantId, dossier.Id, CancellationToken.None);
        Assert.Equal(DossierStatus.NeedsReview, loaded?.Status);
    }

    [Fact]
    public async Task Complete_IsIdempotent()
    {
        var dossier = await AddProcessingDossierAsync();
        var handler = new FinalizeDossierHandler(_dossiers, _clock);
        await handler.CompleteAsync(dossier.TenantId, dossier.Id, CancellationToken.None);
        var versionAfterFirst = dossier.Version;

        await handler.CompleteAsync(dossier.TenantId, dossier.Id, CancellationToken.None);

        Assert.Equal(versionAfterFirst, dossier.Version);
    }

    [Fact]
    public async Task Complete_OnAFailedDossier_IsAnError()
    {
        var dossier = await AddProcessingDossierAsync();
        var handler = new FinalizeDossierHandler(_dossiers, _clock);
        await handler.MarkFailedAsync(dossier.TenantId, dossier.Id, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidDossierTransitionException>(
            () => handler.CompleteAsync(dossier.TenantId, dossier.Id, CancellationToken.None));
    }

    // ---------- Contrato JSON entre as etapas ----------

    [Fact]
    public void DocumentTask_UsesSnakeCase()
    {
        var json = JsonSerializer.Serialize(new DocumentTask("ten_a", "dos_1", "doc_1"), WorkersJsonContext.Default.DocumentTask);

        Assert.Equal("""{"tenant_id":"ten_a","dossier_id":"dos_1","document_id":"doc_1"}""", json);
    }

    [Fact]
    public void FinalizeInput_ReadsTheStepFunctionsCatchFormat()
    {
        const string json = """
            {"tenant_id":"ten_a","dossier_id":"dos_1","outcome":"failed",
             "error":{"Error":"DocumentRejectedException","Cause":"{\"errorMessage\":\"PDF protegido\"}"}}
            """;

        var input = JsonSerializer.Deserialize(json, WorkersJsonContext.Default.FinalizeInput);

        Assert.Equal(FinalizeOutcomes.Failed, input?.Outcome);
        Assert.Equal("DocumentRejectedException", input?.Error?.Error);
    }

    [Fact]
    public void LambdaSerializer_ReadsTheStepFunctionsPayloadInSnakeCase()
    {
        // O mesmo serializador que a Lambda usa em produção (não só o contexto JSON)
        var serializer = WorkerHost.CreateSerializer();
        using var payload = new MemoryStream("""{"tenant_id":"ten_a","dossier_id":"dos_1","document_id":"doc_1"}"""u8.ToArray());

        var task = serializer.Deserialize<DocumentTask>(payload);

        Assert.Equal(new DocumentTask("ten_a", "dos_1", "doc_1"), task);
    }

    [Fact]
    public void LambdaSerializer_WritesTheResultInSnakeCase()
    {
        var serializer = WorkerHost.CreateSerializer();
        using var output = new MemoryStream();

        serializer.Serialize(new IngestResult("doc_1", 3), output);

        Assert.Equal("""{"document_id":"doc_1","page_count":3}""", System.Text.Encoding.UTF8.GetString(output.ToArray()));
    }

    [Fact]
    public void LambdaSerializer_WritesTheExtractResultWithCountsOnly()
    {
        var serializer = WorkerHost.CreateSerializer();
        using var output = new MemoryStream();

        serializer.Serialize(new ExtractResult("doc_1", "holerite.v2", 11, 10, 4, 2, 0, 4398, 1036, 0, 0), output);

        Assert.Equal(
            """{"document_id":"doc_1","schema_id":"holerite.v2","field_count":11,"filled_count":10,"item_count":4,"alert_count":2,"unlocated_count":0,"input_tokens":4398,"output_tokens":1036,"cache_read_tokens":0,"cache_write_tokens":0}""",
            System.Text.Encoding.UTF8.GetString(output.ToArray()));
    }

    private async Task<Dossier> AddProcessingDossierAsync()
    {
        var dossier = Dossier.Create(TenantId.From("ten_teste"), AnalysisType.Income, null, _clock);
        await _dossiers.AddAsync(dossier, CancellationToken.None);
        dossier.Submit(_clock);
        await _dossiers.UpdateAsync(dossier, CancellationToken.None);
        return dossier;
    }
}
