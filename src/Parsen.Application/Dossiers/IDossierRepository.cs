using Parsen.Domain.Dossiers;
using Parsen.Domain.Tenants;

namespace Parsen.Application.Dossiers;

/// <summary>Porta de persistência de dossiês. Toda leitura exige o tenant (regra 2).</summary>
public interface IDossierRepository
{
    Task AddAsync(Dossier dossier, CancellationToken cancellationToken);

    /// <summary>Retorna null se o dossiê não existe ou pertence a outro tenant.</summary>
    Task<Dossier?> GetAsync(TenantId tenantId, DossierId id, CancellationToken cancellationToken);

    /// <summary>
    /// Grava as mudanças só se a versão no banco for a mesma lida (controle otimista); em caso de
    /// sucesso, avança <see cref="Dossier.Version"/>. Outra gravação no meio do caminho gera
    /// <see cref="Common.PersistenceConflictException"/>.
    /// </summary>
    Task UpdateAsync(Dossier dossier, CancellationToken cancellationToken);
}
