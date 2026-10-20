using Parsen.Application.Pipeline;
using Parsen.Domain.Dossiers;
using Parsen.Workers.Ingest;
using SkiaSharp;

namespace Parsen.Workers.Tests;

/// <summary>Usa o PDFium de verdade (há versão para Windows), com PDFs e imagens sintéticos.</summary>
public sealed class PdfiumPageRendererTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly PdfiumPageRenderer _renderer = new();

    [Fact]
    public void Pdf_CountsAndRendersEveryPageAsPng()
    {
        var pdf = SyntheticPdf.WithPages(3);

        Assert.Equal(3, _renderer.CountPages(pdf, DocumentContentType.Pdf));
        var pages = _renderer.Render(pdf, DocumentContentType.Pdf);

        Assert.Equal(3, pages.Count);
        Assert.All(pages, page => Assert.Equal(PngSignature, page[..8]));
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Png, DocumentContentType.Png)]
    [InlineData(SKEncodedImageFormat.Jpeg, DocumentContentType.Jpeg)]
    public void Image_BecomesSinglePngPage(SKEncodedImageFormat format, DocumentContentType contentType)
    {
        var image = SyntheticImage(format);

        Assert.Equal(1, _renderer.CountPages(image, contentType));
        var page = Assert.Single(_renderer.Render(image, contentType));
        Assert.Equal(PngSignature, page[..8]);
    }

    [Fact]
    public void InvalidPdf_IsRejectedAsInvalidFile()
    {
        var ex = Assert.Throws<DocumentRejectedException>(
            () => _renderer.CountPages("isto não é um PDF"u8.ToArray(), DocumentContentType.Pdf));

        Assert.Equal("ARQUIVO_INVALIDO", ex.Code);
    }

    [Fact]
    public void InvalidImage_IsRejectedAsInvalidFile()
    {
        var ex = Assert.Throws<DocumentRejectedException>(
            () => _renderer.Render("isto não é uma imagem"u8.ToArray(), DocumentContentType.Png));

        Assert.Equal("ARQUIVO_INVALIDO", ex.Code);
    }

    private static byte[] SyntheticImage(SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(40, 20);
        bitmap.Erase(SKColors.White);
        using var data = SKImage.FromBitmap(bitmap).Encode(format, 90);
        return data.ToArray();
    }
}
