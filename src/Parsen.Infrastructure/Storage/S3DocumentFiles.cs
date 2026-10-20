using System.Globalization;
using Amazon.S3;
using Amazon.S3.Model;
using Parsen.Application.Pipeline;
using Parsen.Domain.Dossiers;
using Parsen.Infrastructure.Ocr;

namespace Parsen.Infrastructure.Storage;

/// <summary>
/// Arquivos do documento no S3 para os workers do pipeline. As páginas herdam a tag de retenção do
/// original e a criptografia KMS padrão do bucket.
/// </summary>
public sealed class S3DocumentFiles(IAmazonS3 s3, string bucketName) : IDocumentFiles
{
    private const string RetentionTag = "retention-days";

    public async Task<byte[]> ReadOriginalAsync(Document document, CancellationToken cancellationToken)
    {
        using var response = await s3.GetObjectAsync(bucketName, ObjectKeys.Original(document), cancellationToken);
        using var buffer = new MemoryStream();
        await response.ResponseStream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    public async Task<int?> GetRetentionDaysAsync(Document document, CancellationToken cancellationToken)
    {
        var response = await s3.GetObjectTaggingAsync(new GetObjectTaggingRequest
        {
            BucketName = bucketName,
            Key = ObjectKeys.Original(document),
        }, cancellationToken);

        var tag = response.Tagging?.FirstOrDefault(t => t.Key == RetentionTag);
        return int.TryParse(tag?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) ? days : null;
    }

    public async Task SavePageAsync(Document document, int pageNumber, byte[] png, int retentionDays, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(png);
        using var content = new MemoryStream(png);

        await PutAsync(ObjectKeys.Page(document, pageNumber), content, "image/png", retentionDays, cancellationToken);
    }

    public async Task<byte[]> ReadPageAsync(Document document, int pageNumber, CancellationToken cancellationToken)
    {
        using var response = await s3.GetObjectAsync(bucketName, ObjectKeys.Page(document, pageNumber), cancellationToken);
        using var buffer = new MemoryStream();
        await response.ResponseStream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    public async Task SaveOcrAsync(Document document, OcrDocument ocr, int retentionDays, CancellationToken cancellationToken)
    {
        // O texto reconhecido tem dados pessoais: mesma retenção e criptografia KMS do original
        using var content = new MemoryStream(OcrJson.Serialize(ocr));
        await PutAsync(ObjectKeys.Ocr(document), content, "application/json", retentionDays, cancellationToken);
    }

    private async Task PutAsync(string key, Stream content, string contentType, int retentionDays, CancellationToken cancellationToken) =>
        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            TagSet = [new Tag { Key = RetentionTag, Value = retentionDays.ToString(CultureInfo.InvariantCulture) }],
        }, cancellationToken);
}
