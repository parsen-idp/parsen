using Amazon.DynamoDBv2;
using Parsen.Application.Pipeline;
using Parsen.Domain.Dossiers;
using Parsen.Domain.Tenants;
using Parsen.Infrastructure.DynamoDb;
using Parsen.Workers.Common;

var handler = new FinalizeDossierHandler(
    new DynamoDbDossierRepository(new AmazonDynamoDBClient(), new DynamoDbSettings(WorkerHost.RequiredEnvironment("TABLE_NAME"))),
    TimeProvider.System);

await WorkerHost.RunAsync<FinalizeInput, FinalizeResult>(async (input, context) =>
{
    using var cancellation = new CancellationTokenSource(context.RemainingTime - TimeSpan.FromSeconds(2));
    var tenantId = TenantId.From(input.TenantId);
    var dossierId = DossierId.Parse(input.DossierId);

    switch (input.Outcome)
    {
        case FinalizeOutcomes.Failed:
            // Só o tipo do erro no log: a mensagem (Cause) pode trazer detalhes internos
            context.Logger.LogWarning($"Pipeline falhou: dossier={input.DossierId} error={input.Error?.Error}");
            await handler.MarkFailedAsync(tenantId, dossierId, cancellation.Token);
            return new FinalizeResult(input.DossierId, FinalizeOutcomes.Failed);

        case FinalizeOutcomes.NeedsReview:
            await handler.CompleteAsync(tenantId, dossierId, cancellation.Token);
            context.Logger.LogInformation($"Pipeline concluído: dossier={input.DossierId} status={FinalizeOutcomes.NeedsReview}");
            return new FinalizeResult(input.DossierId, FinalizeOutcomes.NeedsReview);

        default:
            throw new InvalidOperationException($"Resultado de pipeline não suportado: '{input.Outcome}'.");
    }
});
