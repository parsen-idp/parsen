using System.Collections.Concurrent;
using Parsen.Application.Documents;
using Parsen.Domain.Dossiers;
using Parsen.Domain.Tenants;

namespace Parsen.Infrastructure.Documents;

/// <summary>Repositório de documentos em memória para desenvolvimento local e testes.</summary>
public sealed class InMemoryDocumentRepository : IDocumentRepository
{
    private readonly ConcurrentDictionary<(TenantId, DossierId), ConcurrentQueue<Document>> _items = new();

    public Task AddAsync(Document document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        _items.GetOrAdd((document.TenantId, document.DossierId), _ => new()).Enqueue(document);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Document>> ListByDossierAsync(TenantId tenantId, DossierId dossierId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Document>>(
            _items.TryGetValue((tenantId, dossierId), out var documents) ? [.. documents] : []);
}
