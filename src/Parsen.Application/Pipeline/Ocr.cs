using Parsen.Application.Documents;
using Parsen.Domain.Dossiers;
using Parsen.Domain.Tenants;

namespace Parsen.Application.Pipeline;

// Resultado do OCR no formato da Parsen (não o da AWS): o Extract e a evidência por campo
// dependem só deste modelo. bbox = [esquerda, topo, largura, altura], em frações da página (0 a 1).

public sealed record OcrDocument(IReadOnlyList<OcrPage> Pages);

public sealed record OcrPage(int Page, IReadOnlyList<OcrLine> Lines);

public sealed record OcrLine(string Text, double Confidence, IReadOnlyList<double> Bbox, IReadOnlyList<OcrWord> Words);

public sealed record OcrWord(string Text, double Confidence, IReadOnlyList<double> Bbox);

/// <summary>Reconhece o texto de uma página (Textract na infraestrutura).</summary>
public interface IOcrEngine
{
    /// <exception cref="DocumentRejectedException">Imagem que o OCR não aceita (não adianta tentar de novo).</exception>
    Task<OcrPage> RecognizeAsync(byte[] png, int pageNumber, CancellationToken cancellationToken);
}

/// <summary>
/// Etapa de OCR (spec, seção 5): reconhece cada página renderizada pelo Ingest e grava o resultado
/// (textract.json) ao lado do original, com a mesma retenção. Páginas uma a uma, para respeitar a cota do Textract.
/// </summary>
public sealed class OcrDocumentHandler(IDocumentRepository documents, IDocumentFiles files, IOcrEngine ocr)
{
    private const int DefaultRetentionDays = 90;

    public async Task<OcrDocument> HandleAsync(
        TenantId tenantId, DossierId dossierId, DocumentId documentId, int pageCount, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageCount, 1);

        var document = await documents.GetAsync(tenantId, dossierId, documentId, cancellationToken)
            ?? throw new InvalidOperationException($"Documento {documentId} não encontrado no dossiê {dossierId}.");

        var pages = new List<OcrPage>(pageCount);
        for (var page = 1; page <= pageCount; page++)
        {
            var png = await files.ReadPageAsync(document, page, cancellationToken);
            pages.Add(await ocr.RecognizeAsync(png, page, cancellationToken));
        }

        var result = new OcrDocument(pages);
        var retentionDays = await files.GetRetentionDaysAsync(document, cancellationToken) ?? DefaultRetentionDays;
        await files.SaveOcrAsync(document, result, retentionDays, cancellationToken);
        return result;
    }
}
