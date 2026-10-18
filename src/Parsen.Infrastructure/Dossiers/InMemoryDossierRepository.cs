using System.Collections.Concurrent;
using Parsen.Application.Dossiers;
using Parsen.Domain.Dossiers;
using Parsen.Domain.Tenants;

namespace Parsen.Infrastructure.Dossiers;

/// <summary>Repositório em memória para desenvolvimento local; substituído pelo DynamoDB no passo 5b.</summary>
public sealed class InMemoryDossierRepository : IDossierRepository
{
    private readonly ConcurrentDictionary<(TenantId, DossierId), Dossier> _items = new();

    public Task AddAsync(Dossier dossier, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dossier);
        _items[(dossier.TenantId, dossier.Id)] = dossier;
        return Task.CompletedTask;
    }

    public Task<Dossier?> GetAsync(TenantId tenantId, DossierId id, CancellationToken cancellationToken) =>
        Task.FromResult(_items.GetValueOrDefault((tenantId, id)));

    /// <summary>Em memória o objeto já é o próprio registro: basta avançar a versão.</summary>
    public Task UpdateAsync(Dossier dossier, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dossier);
        _items[(dossier.TenantId, dossier.Id)] = dossier;
        dossier.MarkSaved();
        return Task.CompletedTask;
    }
}
