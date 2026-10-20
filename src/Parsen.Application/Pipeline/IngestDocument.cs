using Parsen.Application.Documents;
using Parsen.Domain.Dossiers;
using Parsen.Domain.Tenants;

namespace Parsen.Application.Pipeline;

/// <summary>
/// Etapa 1 do pipeline (spec, seção 5): renderiza cada página do original em PNG e grava no
/// armazenamento com a mesma retenção do original. Devolve a quantidade de páginas.
/// </summary>
public sealed class IngestDocumentHandler(IDocumentRepository documents, IDocumentFiles files, IPageRenderer renderer)
{
    /// <summary>Retenção padrão da spec, se o original vier sem a tag (não deveria: a URL pré-assinada exige).</summary>
    private const int DefaultRetentionDays = 90;

    public async Task<int> HandleAsync(TenantId tenantId, DossierId dossierId, DocumentId documentId, CancellationToken cancellationToken)
    {
        var document = await documents.GetAsync(tenantId, dossierId, documentId, cancellationToken)
            ?? throw new InvalidOperationException($"Documento {documentId} não encontrado no dossiê {dossierId}.");

        var original = await files.ReadOriginalAsync(document, cancellationToken);
        var retentionDays = await files.GetRetentionDaysAsync(document, cancellationToken) ?? DefaultRetentionDays;

        // Confere o limite antes de renderizar: recusar um PDF enorme não deve custar a renderização inteira
        var pageCount = renderer.CountPages(original, document.ContentType);
        if (pageCount == 0)
        {
            throw new DocumentRejectedException("DOCUMENTO_SEM_PAGINAS", $"O documento {documentId} não tem páginas.");
        }

        if (pageCount > UploadLimits.MaxPagesPerDossier)
        {
            throw new DocumentRejectedException(
                "PAGINAS_EXCEDIDAS", $"O documento {documentId} tem {pageCount} páginas; o limite é {UploadLimits.MaxPagesPerDossier}.");
        }

        var pages = renderer.Render(original, document.ContentType);
        for (var i = 0; i < pages.Count; i++)
        {
            await files.SavePageAsync(document, i + 1, pages[i], retentionDays, cancellationToken);
        }

        return pages.Count;
    }
}
