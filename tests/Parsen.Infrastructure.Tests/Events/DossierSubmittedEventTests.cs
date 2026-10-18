using System.Text.Json;
using Parsen.Application.Dossiers;
using Parsen.Domain.Dossiers;
using Parsen.Domain.Tenants;
using Parsen.Infrastructure.Events;

namespace Parsen.Infrastructure.Tests.Events;

public sealed class DossierSubmittedEventTests
{
    [Fact]
    public void Detail_HasOnlyIdsAndTimestamp_InSnakeCase()
    {
        var dossierId = DossierId.New();
        var documents = new[] { DocumentId.New(), DocumentId.New() };
        var submitted = new DossierSubmitted(
            TenantId.From("ten_teste"), dossierId, documents, new DateTimeOffset(2026, 10, 18, 15, 0, 0, TimeSpan.Zero));

        var detail = JsonDocument.Parse(DossierSubmittedEvent.ToDetailJson(submitted)).RootElement;

        // Contrato com o pipeline: exatamente estes campos, nenhum dado pessoal (regra 3)
        Assert.Equal(["tenant_id", "dossier_id", "document_ids", "submitted_at"], detail.EnumerateObject().Select(p => p.Name));
        Assert.Equal("ten_teste", detail.GetProperty("tenant_id").GetString());
        Assert.Equal(dossierId.ToString(), detail.GetProperty("dossier_id").GetString());
        Assert.Equal(documents.Select(d => d.ToString()), detail.GetProperty("document_ids").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("2026-10-18T15:00:00Z", detail.GetProperty("submitted_at").GetString());
    }

    [Fact]
    public void Envelope_UsesStableSourceAndDetailType()
    {
        Assert.Equal("parsen.api", DossierSubmittedEvent.Source);
        Assert.Equal("DossierSubmitted", DossierSubmittedEvent.DetailType);
    }
}
