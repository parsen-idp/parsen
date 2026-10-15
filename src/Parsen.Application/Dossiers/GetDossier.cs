using Parsen.Domain.Dossiers;
using Parsen.Domain.Tenants;

namespace Parsen.Application.Dossiers;

public sealed class GetDossierHandler(IDossierRepository repository)
{
    public Task<Dossier?> HandleAsync(TenantId tenantId, DossierId id, CancellationToken cancellationToken) =>
        repository.GetAsync(tenantId, id, cancellationToken);
}
