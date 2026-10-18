using Amazon.DynamoDBv2;
using Amazon.S3;
using Parsen.Application.Pipeline;
using Parsen.Domain.Dossiers;
using Parsen.Domain.Tenants;
using Parsen.Infrastructure.DynamoDb;
using Parsen.Infrastructure.Storage;
using Parsen.Workers.Common;
using Parsen.Workers.Ingest;

// Composição: clientes e dependências criados uma vez por instância da Lambda (cold start)
var handler = new IngestDocumentHandler(
    new DynamoDbDocumentRepository(new AmazonDynamoDBClient(), new DynamoDbSettings(WorkerHost.RequiredEnvironment("TABLE_NAME"))),
    new S3DocumentFiles(new AmazonS3Client(), WorkerHost.RequiredEnvironment("BUCKET_NAME")),
    new PdfiumPageRenderer());

await WorkerHost.RunAsync<DocumentTask, IngestResult>(async (task, context) =>
{
    using var cancellation = new CancellationTokenSource(context.RemainingTime - TimeSpan.FromSeconds(2));

    var pageCount = await handler.HandleAsync(
        TenantId.From(task.TenantId), DossierId.Parse(task.DossierId), DocumentId.Parse(task.DocumentId), cancellation.Token);

    // Só IDs e contagem no log (regra 3)
    context.Logger.LogInformation($"Ingest concluído: dossier={task.DossierId} document={task.DocumentId} pages={pageCount}");
    return new IngestResult(task.DocumentId, pageCount);
});
