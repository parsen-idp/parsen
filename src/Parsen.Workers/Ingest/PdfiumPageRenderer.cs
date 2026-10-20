using Parsen.Application.Pipeline;
using Parsen.Domain.Dossiers;
using PDFtoImage;
using PDFtoImage.Exceptions;
using SkiaSharp;

namespace Parsen.Workers.Ingest;

/// <summary>
/// PDF → PNG por página com PDFium (biblioteca PDFtoImage); JPG/PNG viram a página 1, em PNG.
/// 150 DPI: legível para o Textract e o Claude sem gerar imagens enormes (spike A).
/// </summary>
public sealed class PdfiumPageRenderer : IPageRenderer
{
    private static readonly RenderOptions Options = new(Dpi: 150);

    public int CountPages(byte[] content, DocumentContentType contentType) => contentType switch
    {
        DocumentContentType.Pdf => Guard(() => Conversion.GetPageCount(content)),
        DocumentContentType.Jpeg or DocumentContentType.Png => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(contentType), contentType, null),
    };

    public IReadOnlyList<byte[]> Render(byte[] content, DocumentContentType contentType) => contentType switch
    {
        DocumentContentType.Pdf => Guard(() => RenderPdf(content)),
        DocumentContentType.Jpeg or DocumentContentType.Png => [ToPng(content)],
        _ => throw new ArgumentOutOfRangeException(nameof(contentType), contentType, null),
    };

    private static List<byte[]> RenderPdf(byte[] pdf)
    {
        var count = Conversion.GetPageCount(pdf);
        var pages = new List<byte[]>(count);
        for (var page = 0; page < count; page++)
        {
            using var png = new MemoryStream();
            Conversion.SavePng(png, pdf, page: page, options: Options);
            pages.Add(png.ToArray());
        }

        return pages;
    }

    private static byte[] ToPng(byte[] image)
    {
        // SKCodec.Create devolve null para formato desconhecido; SKBitmap.Decode(byte[]) lançaria ArgumentNullException
        using var data = SKData.CreateCopy(image);
        using var codec = SKCodec.Create(data) ?? throw InvalidImage();
        using var bitmap = SKBitmap.Decode(codec) ?? throw InvalidImage();
        using var encoded = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    private static DocumentRejectedException InvalidImage() =>
        new("ARQUIVO_INVALIDO", "A imagem enviada não pôde ser lida.");

    /// <summary>
    /// Traduz falhas do PDFium em recusas de regra: arquivo com senha ou corrompido não melhora com retry.
    /// Demais erros (ex.: PdfUnknownException) seguem como falha técnica, e o Step Functions tenta de novo.
    /// </summary>
    private static T Guard<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (ex is PdfPasswordProtectedException or PdfUnsupportedSecuritySchemeException)
        {
            throw new DocumentRejectedException("PDF_PROTEGIDO", "PDF protegido por senha não é aceito.");
        }
        catch (Exception ex) when (ex is PdfInvalidFormatException or PdfCannotOpenFileException)
        {
            throw new DocumentRejectedException("ARQUIVO_INVALIDO", "O PDF enviado não pôde ser lido.");
        }
    }
}
