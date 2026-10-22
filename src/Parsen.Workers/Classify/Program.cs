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

// Custo do Claude é real (CLAUDE.md): o SDK tenta de novo no máximo 2 vezes; a resposta é curta (tipos e páginas)
var bedrock = new AmazonBedrockRuntimeClient(new AmazonBedrockRuntimeConfig { MaxErrorRetry = 2 });

var handler = new ClassifyDocumentHandler(
    new DynamoDbDocumentRepository(new AmazonDynamoDBClient(), new DynamoDbSettings(WorkerHost.RequiredEnvironment("TABLE_NAME"))),
    new S3DocumentFiles(new AmazonS3Client(), WorkerHost.RequiredEnvironment("BUCKET_NAME")),
    new BedrockDocumentClassifier(bedrock, new BedrockExtractionSettings(WorkerHost.RequiredEnvironment("MODEL_ID"), MaxTokens: 1024)),
    TimeProvider.System);

await WorkerHost.RunAsync<DocumentTask, ClassifyResult>(async (task, context) =>
{
    using var cancellation = new CancellationTokenSource(context.RemainingTime - TimeSpan.FromSeconds(2));

    var result = await handler.HandleAsync(
        TenantId.From(task.TenantId), DossierId.Parse(task.DossierId), DocumentId.Parse(task.DocumentId), cancellation.Token);

    // Tipos e contagens no log; os alertas do modelo não, porque podem citar conteúdo do documento (regra 3)
    var documents = result.Documents.Select(d => new ClassifiedDocument(d.Id.ToString(), DocumentTypeCodes.ToCode(d.Type!.Value))).ToList();
    context.Logger.LogInformation(
        $"Classificação concluída: dossier={task.DossierId} document={task.DocumentId} reused={result.Reused} " +
        $"documents={string.Join(",", documents.Select(d => d.Type))} alerts={result.Alerts.Count} " +
        $"tokens_in={result.Usage.InputTokens} tokens_out={result.Usage.OutputTokens}");

    return new ClassifyResult(task.DocumentId, documents, result.Alerts.Count, result.Reused, result.Usage.InputTokens, result.Usage.OutputTokens);
});
