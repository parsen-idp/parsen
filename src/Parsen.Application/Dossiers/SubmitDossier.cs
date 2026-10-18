using Parsen.Application.Common;
using Parsen.Application.Documents;
using Parsen.Domain.Dossiers;
using Parsen.Domain.Tenants;

namespace Parsen.Application.Dossiers;

/// <summary>
/// Fecha o dossiê e inicia o pipeline (spec, seção 7): confere os arquivos no armazenamento,
/// grava o status processing e publica DossierSubmitted. Se a publicação falhar, desfaz o status.
/// </summary>
public sealed class SubmitDossierHandler(
    IDossierRepository dossiers,
    IDocumentRepository documents,
    IFileStorage storage,
    IEventPublisher events,
    TimeProvider clock)
{
    public async Task<Dossier> HandleAsync(TenantId tenantId, DossierId dossierId, CancellationToken cancellationToken)
    {
        var dossier = await dossiers.GetAsync(tenantId, dossierId, cancellationToken)
            ?? throw new RequestRejectedException("DOSSIE_NAO_ENCONTRADO", "Dossiê não encontrado.", RejectionKind.NotFound);

        // Confere o status antes de consultar o armazenamento: evita chamadas inúteis ao S3
        if (dossier.Status != DossierStatus.Draft)
        {
            throw new InvalidDossierTransitionException(dossier.Status, nameof(Dossier.Submit));
        }

        var dossierDocuments = await documents.ListByDossierAsync(tenantId, dossierId, cancellationToken);
        if (dossierDocuments.Count == 0)
        {
            throw new RequestRejectedException("DOSSIE_SEM_DOCUMENTOS", "Envie ao menos um documento antes de submeter.");
        }

        foreach (var document in dossierDocuments)
        {
            var stored = await storage.GetStoredFileAsync(document, cancellationToken)
                ?? throw new RequestRejectedException("ARQUIVO_NAO_ENVIADO", $"O arquivo do documento {document.Id} não foi enviado.");

            // A URL pré-assinada não limita o tamanho: o real é conferido aqui
            if (stored.SizeBytes > UploadLimits.MaxFileSizeBytes)
            {
                throw new RequestRejectedException("TAMANHO_EXCEDIDO", $"O arquivo do documento {document.Id} passa de 20 MB.");
            }
        }

        dossier.Submit(clock);
        await dossiers.UpdateAsync(dossier, cancellationToken);

        var submitted = new DossierSubmitted(tenantId, dossierId, [.. dossierDocuments.Select(d => d.Id)], dossier.UpdatedAt);
        try
        {
            await events.PublishAsync(submitted, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Sem o evento, ninguém processaria o dossiê: volta para rascunho e o cliente tenta de novo
            dossier.RevertSubmit(clock);
            await dossiers.UpdateAsync(dossier, CancellationToken.None);

            // Recusa já explicada (ex.: integração não configurada) segue com o próprio código
            if (ex is RequestRejectedException)
            {
                throw;
            }

            throw new RequestRejectedException(
                "TENTE_NOVAMENTE", "Não foi possível iniciar o processamento. Tente novamente.", RejectionKind.Unavailable, ex);
        }

        return dossier;
    }
}
