using System.Web;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Time.Testing;
using Parsen.Domain.Dossiers;
using Parsen.Domain.Tenants;
using Parsen.Infrastructure.Storage;

namespace Parsen.Infrastructure.Tests.Storage;

/// <summary>
/// Assinar uma URL pré-assinada é um cálculo local: estes testes não chamam o S3 nem precisam de rede.
/// </summary>
public sealed class S3FileStorageTests : IDisposable
{
    // O SDK calcula a validade (X-Amz-Expires) contra o relógio real: o relógio do teste parte dele
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private const string KeyArn = "arn:aws:kms:sa-east-1:111122223333:key/chave-de-teste";

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly AmazonS3Client _s3 = new(new BasicAWSCredentials("AKIDEXAMPLE", "segredo-de-teste"), RegionEndpoint.SAEast1);
    private readonly S3FileStorage _storage;

    public S3FileStorageTests() =>
        _storage = new S3FileStorage(_s3, new S3StorageSettings("parsen-docs-teste", KeyArn), _clock);

    public void Dispose() => _s3.Dispose();

    [Theory]
    [InlineData(DocumentContentType.Pdf, "original.pdf")]
    [InlineData(DocumentContentType.Jpeg, "original.jpg")]
    [InlineData(DocumentContentType.Png, "original.png")]
    public void ObjectKey_IsUnderTenantDossierAndDocument(DocumentContentType type, string fileName)
    {
        var document = NewDocument(type);

        var key = ObjectKeys.Original(document);

        Assert.Equal($"ten_teste/{document.DossierId}/{document.Id}/{fileName}", key);
    }

    [Fact]
    public void PageKey_IsUnderTheDocumentPagesFolder()
    {
        var document = NewDocument(DocumentContentType.Pdf);

        Assert.Equal($"ten_teste/{document.DossierId}/{document.Id}/pages/3.png", ObjectKeys.Page(document, 3));
        Assert.Equal($"ten_teste/{document.DossierId}/{document.Id}/textract.json", ObjectKeys.Ocr(document));
        Assert.Throws<ArgumentOutOfRangeException>(() => ObjectKeys.Page(document, 0));
    }

    [Fact]
    public async Task CreateUpload_ReturnsPutUrlForTheDocumentKey()
    {
        var document = NewDocument(DocumentContentType.Pdf);

        var ticket = await _storage.CreateUploadAsync(document, CancellationToken.None);

        Assert.Equal("PUT", ticket.Method);
        Assert.Equal(Uri.UriSchemeHttps, ticket.Url.Scheme);
        Assert.Contains("parsen-docs-teste", ticket.Url.Host + ticket.Url.AbsolutePath, StringComparison.Ordinal);
        Assert.EndsWith($"{document.Id}/original.pdf", ticket.Url.AbsolutePath, StringComparison.Ordinal);
        Assert.Equal(Now.AddMinutes(15), ticket.ExpiresAt);

        // 15 minutos (900 s), com margem para o tempo de execução do próprio teste
        var expiresInSeconds = int.Parse(Query(ticket.Url)["X-Amz-Expires"]!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(expiresInSeconds, 890, 900);
    }

    [Fact]
    public async Task CreateUpload_SignsTheMandatoryHeaders()
    {
        var ticket = await _storage.CreateUploadAsync(NewDocument(DocumentContentType.Png), CancellationToken.None);

        // Cabeçalhos assinados: o S3 recusa o upload se o cliente não enviar exatamente estes valores
        var signed = Query(ticket.Url)["X-Amz-SignedHeaders"]!.Split(';');
        Assert.Contains("content-type", signed);
        Assert.Contains("x-amz-server-side-encryption", signed);
        Assert.Contains("x-amz-server-side-encryption-aws-kms-key-id", signed);
        Assert.Contains("x-amz-tagging", signed);

        Assert.Equal("image/png", ticket.Headers["Content-Type"]);
        Assert.Equal("aws:kms", ticket.Headers["x-amz-server-side-encryption"]);
        Assert.Equal(KeyArn, ticket.Headers["x-amz-server-side-encryption-aws-kms-key-id"]);
        Assert.Equal("retention-days=90", ticket.Headers["x-amz-tagging"]);
    }

    private static Document NewDocument(DocumentContentType type) =>
        Document.Create(TenantId.From("ten_teste"), DossierId.New(), type, 1_000, TimeProvider.System);

    private static System.Collections.Specialized.NameValueCollection Query(Uri url) => HttpUtility.ParseQueryString(url.Query);
}
