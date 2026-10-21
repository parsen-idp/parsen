using System.Globalization;

namespace Parsen.Application.Pipeline;

// Extração no formato da Parsen, independente do modelo e do Bedrock. O modelo devolve valor, página e
// trecho_fonte; a evidência (bbox) e a confiança saem do OCR, de forma determinística (spec, seção 6).

/// <summary>Valor extraído como texto exato: número (sempre com ponto decimal) ou texto. Vira decimal só no domínio.</summary>
public sealed record FieldValue(string Raw, FieldValueKind Kind)
{
    public static FieldValue Text(string value) => new(value, FieldValueKind.Text);

    public static FieldValue Number(string value) => new(value, FieldValueKind.Number);

    public decimal ToDecimal() => decimal.Parse(Raw, NumberStyles.Number, CultureInfo.InvariantCulture);
}

public enum FieldValueKind
{
    Text,
    Number,
}

/// <summary>Campo como o modelo devolveu: o valor e de onde ele diz que leu.</summary>
public sealed record ModelField(FieldValue Value, int Page, string SourceText);

/// <summary>Linha de uma lista (ex.: rubrica do holerite): atributos (null quando ausentes) e de onde foi lida.</summary>
public sealed record ModelItem(IReadOnlyDictionary<string, FieldValue?> Attributes, int Page, string SourceText);

/// <summary>Saída do modelo. Campos ausentes no documento vêm como null.</summary>
/// <param name="SchemaId">Schema e prompt usados (ex.: holerite.v2).</param>
/// <param name="ModelId">Modelo que extraiu (ID do inference profile).</param>
public sealed record ModelExtraction(
    string SchemaId,
    string ModelId,
    IReadOnlyDictionary<string, ModelField?> Fields,
    IReadOnlyDictionary<string, IReadOnlyList<ModelItem>> Lists,
    IReadOnlyList<string> Alerts);

/// <summary>Onde o dado está no documento. bbox = [esquerda, topo, largura, altura] em frações da página; null se o trecho não foi achado.</summary>
public sealed record Evidence(int Page, string Text, IReadOnlyList<double>? Bbox);

/// <param name="Confidence">De 0 a 1: menor confiança do OCR entre as palavras do trecho; 0 se o trecho não foi achado.</param>
public sealed record LocatedEvidence(Evidence Evidence, double Confidence);

/// <summary>Campo pronto para gravar. Value, Evidence e Confidence são null quando o campo não aparece no documento.</summary>
public sealed record ExtractedField(string Name, FieldValue? Value, Evidence? Evidence, double? Confidence);

public sealed record ExtractedItem(IReadOnlyDictionary<string, FieldValue?> Attributes, Evidence Evidence, double Confidence);

public sealed record ExtractionResult(
    string DocumentType,
    string SchemaId,
    string ModelId,
    IReadOnlyList<ExtractedField> Fields,
    IReadOnlyDictionary<string, IReadOnlyList<ExtractedItem>> Lists,
    IReadOnlyList<string> Alerts,
    DateTimeOffset ExtractedAt);

/// <summary>Extrai os campos de um documento a partir do texto do OCR (Claude via Bedrock na infraestrutura).</summary>
public interface IExtractionEngine
{
    /// <exception cref="DocumentRejectedException">Documento que o modelo não extrai (ex.: recusa); não adianta tentar de novo.</exception>
    Task<ModelExtraction> ExtractAsync(string documentType, OcrDocument ocr, CancellationToken cancellationToken);
}

/// <summary>Casa o trecho_fonte do modelo com as linhas e palavras do OCR.</summary>
public static class EvidenceLocator
{
    public static LocatedEvidence Locate(OcrDocument ocr, int page, string sourceText)
    {
        ArgumentNullException.ThrowIfNull(ocr);
        var snippet = Normalize(sourceText);

        // Primeiro a página que o modelo indicou; se ele errou a página, as demais em ordem
        var pages = ocr.Pages.Where(p => p.Page == page).Concat(ocr.Pages.Where(p => p.Page != page));
        foreach (var ocrPage in pages)
        {
            foreach (var line in ocrPage.Lines)
            {
                if (!Normalize(line.Text).Contains(snippet, StringComparison.Ordinal))
                {
                    continue;
                }

                var words = FindWords(line.Words, snippet.Split(' '));
                return words is null
                    ? new LocatedEvidence(new Evidence(ocrPage.Page, snippet, line.Bbox), Scale(line.Confidence))
                    : new LocatedEvidence(new Evidence(ocrPage.Page, snippet, Union(words)), Scale(words.Min(w => w.Confidence)));
            }
        }

        return new LocatedEvidence(new Evidence(page, snippet, null), 0);
    }

    /// <summary>Sequência de palavras idêntica ao trecho; null se o trecho cai no meio de uma palavra (ex.: "R$1.000,00").</summary>
    private static List<OcrWord>? FindWords(IReadOnlyList<OcrWord> words, string[] tokens)
    {
        for (var start = 0; start + tokens.Length <= words.Count; start++)
        {
            var span = words.Skip(start).Take(tokens.Length).ToList();
            if (span.Select(w => w.Text).SequenceEqual(tokens, StringComparer.Ordinal))
            {
                return span;
            }
        }

        return null;
    }

    private static double[] Union(List<OcrWord> words)
    {
        var left = words.Min(w => w.Bbox[0]);
        var top = words.Min(w => w.Bbox[1]);
        var right = words.Max(w => w.Bbox[0] + w.Bbox[2]);
        var bottom = words.Max(w => w.Bbox[1] + w.Bbox[3]);
        return [Math.Round(left, 4), Math.Round(top, 4), Math.Round(right - left, 4), Math.Round(bottom - top, 4)];
    }

    // O Textract dá a confiança de 0 a 100; a API da Parsen usa de 0 a 1
    private static double Scale(double ocrConfidence) => Math.Round(ocrConfidence / 100, 2);

    private static string Normalize(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
