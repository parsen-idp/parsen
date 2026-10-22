using Parsen.Domain.Dossiers;

namespace Parsen.Application.Pipeline;

/// <summary>Um documento encontrado no arquivo: o tipo e as páginas que ocupa (numeradas pelo arquivo enviado).</summary>
public sealed record ClassifiedPart(DocumentType Type, PageRange Pages);

/// <param name="SchemaId">Schema e prompt usados (ex.: classificacao.v1).</param>
public sealed record ClassificationOutput(
    string SchemaId,
    string ModelId,
    IReadOnlyList<ClassifiedPart> Parts,
    IReadOnlyList<string> Alerts,
    ModelUsage Usage);

/// <summary>Split e classificação de um arquivo a partir do OCR (Claude via Bedrock na infraestrutura).</summary>
public interface IDocumentClassifier
{
    /// <exception cref="DocumentRejectedException">Resposta cortada ou recusada; não adianta tentar de novo.</exception>
    Task<ClassificationOutput> ClassifyAsync(OcrDocument ocr, CancellationToken cancellationToken);
}
