using Microsoft.Extensions.Time.Testing;
using Parsen.Domain.Dossiers;
using Parsen.Domain.Tenants;

namespace Parsen.Domain.Tests.Dossiers;

public class DocumentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly TenantId Tenant = TenantId.From("ten_teste");

    private readonly FakeTimeProvider _clock = new(Now);

    // ---------- Limites de upload (spec, seção 7) ----------

    [Theory]
    [InlineData("application/pdf", DocumentContentType.Pdf)]
    [InlineData("image/jpeg", DocumentContentType.Jpeg)]
    [InlineData("image/png", DocumentContentType.Png)]
    [InlineData("APPLICATION/PDF", DocumentContentType.Pdf)]
    public void TryParseContentType_AcceptsSupportedFormats(string mime, DocumentContentType expected)
    {
        Assert.True(UploadLimits.TryParseContentType(mime, out var type));
        Assert.Equal(expected, type);
    }

    [Theory]
    [InlineData("image/heic")]
    [InlineData("application/msword")]
    [InlineData("text/plain")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParseContentType_RejectsUnsupportedFormats(string? mime)
    {
        Assert.False(UploadLimits.TryParseContentType(mime, out _));
    }

    [Fact]
    public void MaxFileSize_Is20Megabytes()
    {
        Assert.Equal(20L * 1024 * 1024, UploadLimits.MaxFileSizeBytes);
    }

    // ---------- Criação ----------

    [Fact]
    public void Create_KeepsMetadataAndUtcTimestamp()
    {
        var dossierId = DossierId.New();

        var document = Document.Create(Tenant, dossierId, DocumentContentType.Pdf, 53_979, _clock);

        Assert.StartsWith("doc_", document.Id.ToString(), StringComparison.Ordinal);
        Assert.Equal(Tenant, document.TenantId);
        Assert.Equal(dossierId, document.DossierId);
        Assert.Equal(DocumentContentType.Pdf, document.ContentType);
        Assert.Equal(53_979, document.DeclaredSizeBytes);
        Assert.Equal(Now, document.CreatedAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(20L * 1024 * 1024 + 1)]
    public void Create_WithSizeOutOfLimits_Throws(long size)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Document.Create(Tenant, DossierId.New(), DocumentContentType.Pdf, size, _clock));
    }

    [Fact]
    public void Create_AtExactLimit_IsAccepted()
    {
        var document = Document.Create(Tenant, DossierId.New(), DocumentContentType.Png, UploadLimits.MaxFileSizeBytes, _clock);

        Assert.Equal(UploadLimits.MaxFileSizeBytes, document.DeclaredSizeBytes);
    }
}
