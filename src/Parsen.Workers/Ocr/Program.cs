using Amazon;
using Amazon.DynamoDBv2;
using Amazon.S3;
using Amazon.Textract;
using Parsen.Application.Pipeline;
using Parsen.Domain.Dossiers;
using Parsen.Domain.Tenants;
using Parsen.Infrastructure.DynamoDb;
using Parsen.Infrastructure.Ocr;
using Parsen.Infrastructure.Storage;
using Parsen.Workers.Common;

// O Textract não existe em sa-east-1: o cliente aponta para a região do parâmetro (us-east-1). Os bytes da
// página vão até lá e voltam como texto; nada fica armazenado fora do Brasil (spec, seção 6)
var textractRegion = RegionEndpoint.GetBySystemName(WorkerHost.RequiredEnvironment("TEXTRACT_REGION"));

var handler = new OcrDocumentHandler(
    new DynamoDbDocumentRepository(new AmazonDynamoDBClient(), new DynamoDbSettings(WorkerHost.RequiredEnvironment("TABLE_NAME"))),
    new S3DocumentFiles(new AmazonS3Client(), WorkerHost.RequiredEnvironment("BUCKET_NAME")),
    new TextractOcrEngine(new AmazonTextractClient(textractRegion)));

await WorkerHost.RunAsync<OcrTask, OcrResult>(async (task, context) =>
{
    using var cancellation = new CancellationTokenSource(context.RemainingTime - TimeSpan.FromSeconds(2));

    var ocr = await handler.HandleAsync(
        TenantId.From(task.TenantId), DossierId.Parse(task.DossierId), DocumentId.Parse(task.DocumentId), task.PageCount, cancellation.Token);

    // Só contagens no log, nunca o texto reconhecido (regra 3)
    var lineCount = ocr.Pages.Sum(p => p.Lines.Count);
    context.Logger.LogInformation($"OCR concluído: dossier={task.DossierId} document={task.DocumentId} pages={ocr.Pages.Count} lines={lineCount}");
    return new OcrResult(task.DocumentId, ocr.Pages.Count, lineCount);
});
