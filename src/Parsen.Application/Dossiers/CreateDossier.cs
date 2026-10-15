using Parsen.Application.Common;
using Parsen.Domain.Dossiers;
using Parsen.Domain.Tenants;

namespace Parsen.Application.Dossiers;

public sealed record CreateDossierCommand(TenantId TenantId, AnalysisType AnalysisType, string? ExternalRef);

public sealed class CreateDossierHandler(IDossierRepository repository, TimeProvider clock)
{
    public async Task<Dossier> HandleAsync(CreateDossierCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Spec, seção 7: income_check nasce só por POST /v1/income-checks
        if (command.AnalysisType == AnalysisType.IncomeCheck)
        {
            throw new RequestRejectedException(
                "TIPO_ANALISE_NAO_PERMITIDO",
                "Dossiês de renda expressa são criados só por POST /v1/income-checks.");
        }

        var dossier = Dossier.Create(command.TenantId, command.AnalysisType, command.ExternalRef, clock);
        await repository.AddAsync(dossier, cancellationToken);
        return dossier;
    }
}
