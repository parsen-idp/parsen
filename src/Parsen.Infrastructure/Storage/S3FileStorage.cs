using System.Globalization;
using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Parsen.Application.Documents;
using Parsen.Domain.Dossiers;

namespace Parsen.Infrastructure.Storage;

/// <param name="BucketName">Bucket de documentos (parsen-docs-{stage}-{conta}).</param>
/// <param name="KmsKeyId">ARN da chave KMS da plataforma; as chaves por tenant entram no cadastro de tenant.</param>
/// <param name="DefaultRetentionDays">Prazo da tag retention-days enquanto o tenant não tem configuração própria (spec, seção 11).</param>
public sealed record S3StorageSettings(string BucketName, string KmsKeyId, int DefaultRetentionDays = 90)
{
    /// <summary>Validade da URL de upload: curta, para limitar o uso de uma URL vazada.</summary>
    public TimeSpan UploadUrlTtl { get; init; } = TimeSpan.FromMinutes(15);
}

/// <summary>Caminhos dos arquivos no bucket: sempre sob o prefixo do tenant (regra 2; spec, seção 9).</summary>
public static class ObjectKeys
{
    public static string Original(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var extension = document.ContentType switch
        {
            DocumentContentType.Pdf => "pdf",
            DocumentContentType.Jpeg => "jpg",
            DocumentContentType.Png => "png",
            _ => throw new ArgumentOutOfRangeException(nameof(document), document.ContentType, null),
        };

        return $"{document.TenantId}/{document.DossierId}/{document.FileId}/original.{extension}";
    }

    /// <summary>Resultado do OCR do documento, no formato da Parsen (spec, seção 9).</summary>
    public static string Ocr(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return $"{document.TenantId}/{document.DossierId}/{document.FileId}/textract.json";
    }

    /// <summary>Página renderizada pelo Ingest: pages/{n}.png, com n começando em 1.</summary>
    public static string Page(Document document, int pageNumber)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        return $"{document.TenantId}/{document.DossierId}/{document.FileId}/pages/{pageNumber.ToString(CultureInfo.InvariantCulture)}.png";
    }
}

/// <summary>
/// Upload direto do cliente ao S3 por URL pré-assinada. Os cabeçalhos assinados obrigam o arquivo
/// a chegar com o formato declarado, cifrado com a nossa chave KMS e com a tag de retenção.
/// </summary>
public sealed class S3FileStorage(IAmazonS3 s3, S3StorageSettings settings, TimeProvider clock) : IFileStorage
{
    public async Task<UploadTicket> CreateUploadAsync(Document document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        var expiresAt = clock.GetUtcNow().Add(settings.UploadUrlTtl);
        var tagging = $"retention-days={settings.DefaultRetentionDays.ToString(CultureInfo.InvariantCulture)}";
        var contentType = UploadLimits.ToMimeType(document.ContentType);

        var request = new GetPreSignedUrlRequest
        {
            BucketName = settings.BucketName,
            Key = ObjectKeys.Original(document),
            Verb = HttpVerb.PUT,
            Expires = expiresAt.UtcDateTime,
            ContentType = contentType,
            ServerSideEncryptionMethod = ServerSideEncryptionMethod.AWSKMS,
            ServerSideEncryptionKeyManagementServiceKeyId = settings.KmsKeyId,
        };
        request.Headers["x-amz-tagging"] = tagging;

        // Cálculo local: o SDK assina com as credenciais da Lambda, sem chamar o S3
        var url = await s3.GetPreSignedURLAsync(request);

        var headers = new Dictionary<string, string>
        {
            ["Content-Type"] = contentType,
            ["x-amz-server-side-encryption"] = "aws:kms",
            ["x-amz-server-side-encryption-aws-kms-key-id"] = settings.KmsKeyId,
            ["x-amz-tagging"] = tagging,
        };

        return new UploadTicket(new Uri(url), "PUT", headers, expiresAt);
    }

    public async Task<StoredFile?> GetStoredFileAsync(Document document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        try
        {
            // HeadObject: só os metadados, sem baixar o arquivo
            var metadata = await s3.GetObjectMetadataAsync(settings.BucketName, ObjectKeys.Original(document), cancellationToken);
            return new StoredFile(metadata.ContentLength, metadata.Headers.ContentType);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }
}
