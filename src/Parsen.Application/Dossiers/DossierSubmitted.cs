using Parsen.Domain.Dossiers;
using Parsen.Domain.Tenants;

namespace Parsen.Application.Dossiers;

/// <summary>
/// Evento que inicia o pipeline do dossiê. Só IDs: nenhum dado pessoal sai em eventos (regra 3).
/// </summary>
public sealed record DossierSubmitted(TenantId TenantId, DossierId DossierId, IReadOnlyList<DocumentId> DocumentIds, DateTimeOffset SubmittedAt);

/// <summary>Publicação de eventos de integração (EventBridge na infraestrutura).</summary>
public interface IEventPublisher
{
    Task PublishAsync(DossierSubmitted submitted, CancellationToken cancellationToken);
}
