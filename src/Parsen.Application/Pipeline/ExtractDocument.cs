using Parsen.Application.Documents;
using Parsen.Domain.Dossiers;
using Parsen.Domain.Tenants;

namespace Parsen.Application.Pipeline;

/// <summary>Grava os campos extraídos e o detalhe do documento (versão do schema e do modelo, alertas).</summary>
public interface IExtractionRepository
{
    /// <summary>Substitui a extração anterior do documento, se houver: o retry do Step Functions refaz a etapa sem duplicar.</summary>
    Task SaveAsync(Document document, ExtractionResult result, CancellationToken cancellationToken);
}

/// <summary>
/// Etapa de extração (spec, seção 5): lê o OCR gravado pelo Ocr, pede os campos ao modelo e casa cada
/// trecho_fonte com o OCR para obter evidência e confiança. Os dados extraídos vão só para o repositório;
/// quem chama recebe o resultado para contar, nunca para logar.
/// </summary>
public sealed class ExtractDocumentHandler(
    IDocumentRepository documents,
    IDocumentFiles files,
    IExtractionEngine engine,
    IExtractionRepository extractions,
    TimeProvider clock)
{
    /// <summary>Fase 1: todo documento é tratado como holerite; a classificação entra na Fase 2 (spec, seção 14).</summary>
    public const string DocumentType = "holerite";

    public async Task<ExtractionResult> HandleAsync(
        TenantId tenantId, DossierId dossierId, DocumentId documentId, CancellationToken cancellationToken)
    {
        var document = await documents.GetAsync(tenantId, dossierId, documentId, cancellationToken)
            ?? throw new InvalidOperationException($"Documento {documentId} não encontrado no dossiê {dossierId}.");

        var ocr = await files.ReadOcrAsync(document, cancellationToken);
        var output = await engine.ExtractAsync(DocumentType, ocr, cancellationToken);

        var result = ExtractionAssembler.Assemble(DocumentType, output, ocr, clock.GetUtcNow());
        await extractions.SaveAsync(document, result, cancellationToken);
        return result;
    }
}

/// <summary>
/// Saída do modelo + OCR → resultado com evidência e confiança. Usado pelo pipeline e pelo Parsen.Eval,
/// para que a avaliação meça exatamente o que o pipeline grava.
/// </summary>
public static class ExtractionAssembler
{
    public static ExtractionResult Assemble(string documentType, ModelExtraction output, OcrDocument ocr, DateTimeOffset extractedAt)
    {
        ArgumentNullException.ThrowIfNull(output);

        var fields = output.Fields
            .Select(f => f.Value is { } field ? Locate(f.Key, field, ocr) : new ExtractedField(f.Key, null, null, null))
            .ToList();

        var lists = output.Lists.ToDictionary(
            l => l.Key,
            l => (IReadOnlyList<ExtractedItem>)l.Value.Select(item => Locate(item, ocr)).ToList());

        return new ExtractionResult(documentType, output.SchemaId, output.ModelId, fields, lists, output.Alerts, output.Usage, extractedAt);
    }

    private static ExtractedField Locate(string name, ModelField field, OcrDocument ocr)
    {
        var located = EvidenceLocator.Locate(ocr, field.Page, field.SourceText);

        // Identificadores saem do texto do OCR quando o trecho foi achado: o modelo localiza bem, mas reescreve mal
        // sequências de letras e dígitos (CNPJ alfanumérico, no Parsen.Eval)
        var value = located.Confidence > 0 && IdentifierFromSource.For(name, field.SourceText) is { } fromSource
            ? fromSource
            : field.Value;

        return new ExtractedField(name, value, located.Evidence, located.Confidence);
    }

    private static ExtractedItem Locate(ModelItem item, OcrDocument ocr)
    {
        var located = EvidenceLocator.Locate(ocr, item.Page, item.SourceText);
        return new ExtractedItem(item.Attributes, located.Evidence, located.Confidence);
    }
}
