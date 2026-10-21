using Amazon.BedrockRuntime;
using Amazon.DynamoDBv2;
using Amazon.S3;
using Parsen.Application.Pipeline;
using Parsen.Domain.Dossiers;
using Parsen.Domain.Tenants;
using Parsen.Infrastructure.DynamoDb;
using Parsen.Infrastructure.Extraction;
using Parsen.Infrastructure.Storage;
using Parsen.Workers.Common;

var tableSettings = new DynamoDbSettings(WorkerHost.RequiredEnvironment("TABLE_NAME"));

// Custo do Claude é real (CLAUDE.md): o SDK tenta de novo no máximo 2 vezes (throttling, rede);
// o Step Functions faz até 3 novas tentativas da etapa inteira
var bedrock = new AmazonBedrockRuntimeClient(new AmazonBedrockRuntimeConfig { MaxErrorRetry = 2 });

var handler = new ExtractDocumentHandler(
    new DynamoDbDocumentRepository(new AmazonDynamoDBClient(), tableSettings),
    new S3DocumentFiles(new AmazonS3Client(), WorkerHost.RequiredEnvironment("BUCKET_NAME")),
    new BedrockExtractionEngine(bedrock, new BedrockExtractionSettings(WorkerHost.RequiredEnvironment("MODEL_ID"))),
    new DynamoDbExtractionRepository(new AmazonDynamoDBClient(), tableSettings),
    TimeProvider.System);

await WorkerHost.RunAsync<DocumentTask, ExtractResult>(async (task, context) =>
{
    using var cancellation = new CancellationTokenSource(context.RemainingTime - TimeSpan.FromSeconds(2));

    var result = await handler.HandleAsync(
        TenantId.From(task.TenantId), DossierId.Parse(task.DossierId), DocumentId.Parse(task.DocumentId), cancellation.Token);

    // Só contagens no log e na saída, nunca os valores extraídos (regra 3)
    var items = result.Lists.Values.SelectMany(l => l).ToList();
    var unlocated = result.Fields.Count(f => f.Confidence == 0) + items.Count(i => i.Confidence == 0);
    var summary = new ExtractResult(
        task.DocumentId,
        result.SchemaId,
        result.Fields.Count,
        result.Fields.Count(f => f.Value is not null),
        items.Count,
        result.Alerts.Count,
        unlocated,
        result.Usage.InputTokens,
        result.Usage.OutputTokens,
        result.Usage.CacheReadTokens,
        result.Usage.CacheWriteTokens);

    context.Logger.LogInformation(
        $"Extração concluída: dossier={task.DossierId} document={task.DocumentId} schema={summary.SchemaId} model={result.ModelId} " +
        $"fields={summary.FilledCount}/{summary.FieldCount} items={summary.ItemCount} alerts={summary.AlertCount} unlocated={summary.UnlocatedCount} " +
        $"tokens_in={summary.InputTokens} tokens_out={summary.OutputTokens} cache_read={summary.CacheReadTokens} cache_write={summary.CacheWriteTokens}");
    return summary;
});
