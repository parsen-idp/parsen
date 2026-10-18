using Parsen.Application.Dossiers;
using Parsen.Domain.Dossiers;
using Parsen.Domain.Tenants;

namespace Parsen.Application.Pipeline;

/// <summary>Última etapa do pipeline: grava o status final do dossiê.</summary>
public sealed class FinalizeDossierHandler(IDossierRepository dossiers, TimeProvider clock)
{
    /// <summary>
    /// Erro definitivo em alguma etapa (após os retries): o dossiê vai para failed. Idempotente: se o
    /// Step Functions repetir esta etapa e o dossiê já estiver em failed, não faz nada.
    /// </summary>
    public async Task MarkFailedAsync(TenantId tenantId, DossierId dossierId, CancellationToken cancellationToken)
    {
        var dossier = await dossiers.GetAsync(tenantId, dossierId, cancellationToken)
            ?? throw new InvalidOperationException($"Dossiê {dossierId} não encontrado.");

        if (dossier.Status == DossierStatus.Failed)
        {
            return;
        }

        dossier.Fail(clock);
        await dossiers.UpdateAsync(dossier, cancellationToken);
    }
}
