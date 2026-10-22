using System.Text;
using System.Text.Json;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Parsen.Application.Pipeline;
using Parsen.Domain.Dossiers;
using Parsen.Schemas;

namespace Parsen.Infrastructure.Extraction;

/// <summary>
/// Split e classificação com o Claude via Bedrock Converse: uma chamada por arquivo, com só o começo de
/// cada página (o cabeçalho identifica o tipo e onde cada documento começa; spec, seção 14).
/// </summary>
public sealed class BedrockDocumentClassifier(IAmazonBedrockRuntime bedrock, BedrockExtractionSettings settings) : IDocumentClassifier
{
    public async Task<ClassificationOutput> ClassifyAsync(OcrDocument ocr, CancellationToken cancellationToken)
    {
        var definition = ExtractionSchemas.Classificacao;
        var request = ConverseExtraction.BuildRequest(definition, settings, ConverseClassification.FormatExcerpts(ocr));
        var response = await bedrock.ConverseAsync(request, cancellationToken);
        return ConverseClassification.ReadResponse(definition, settings.ModelId, response);
    }
}

/// <summary>Texto de entrada e leitura da resposta da classificação, sem rede (testável).</summary>
public static class ConverseClassification
{
    /// <summary>Linhas do começo de cada página enviadas ao modelo: o suficiente para o cabeçalho.</summary>
    public const int LinesPerPage = 20;

    public static string FormatExcerpts(OcrDocument ocr, int linesPerPage = LinesPerPage)
    {
        ArgumentNullException.ThrowIfNull(ocr);
        var text = new StringBuilder();
        foreach (var page in ocr.Pages)
        {
            var lines = page.Lines.Take(linesPerPage).ToList();
            text.Append("=== PÁGINA ").Append(page.Page).Append(" (").Append(lines.Count).Append(" de ").Append(page.Lines.Count).Append(" linhas) ===\n");
            foreach (var line in lines)
            {
                text.Append(line.Text).Append('\n');
            }
        }

        return text.ToString();
    }

    /// <exception cref="DocumentRejectedException">Resposta cortada ou recusada.</exception>
    /// <exception cref="InvalidDataException">Saída malformada (tipo desconhecido, página inválida): erro técnico.</exception>
    public static ClassificationOutput ReadResponse(ExtractionDefinition definition, string modelId, ConverseResponse response)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(response);

        using var json = ConverseExtraction.ReadToolInput(definition, response);
        var root = json.RootElement;

        var parts = root.TryGetProperty("documentos", out var documents) && documents.ValueKind == JsonValueKind.Array
            ? documents.EnumerateArray().Select(ParsePart).ToList()
            : throw new InvalidDataException("Classificação sem a lista de documentos.");

        IReadOnlyList<string> alerts = root.TryGetProperty("alertas", out var alertsElement) && alertsElement.ValueKind == JsonValueKind.Array
            ? [.. alertsElement.EnumerateArray().Select(a => a.GetString() ?? "")]
            : [];

        return new ClassificationOutput(definition.Id, modelId, parts, alerts, ConverseExtraction.ToUsage(response.Usage));
    }

    private static ClassifiedPart ParsePart(JsonElement item)
    {
        var code = item.TryGetProperty("tipo", out var tipo) ? tipo.GetString() : null;
        if (!DocumentTypeCodes.TryParse(code, out var type))
        {
            throw new InvalidDataException($"Tipo de documento desconhecido na classificação: '{code}'.");
        }

        if (!item.TryGetProperty("pagina_inicial", out var first) || !first.TryGetInt32(out var firstPage)
            || !item.TryGetProperty("pagina_final", out var last) || !last.TryGetInt32(out var lastPage)
            || firstPage < 1 || lastPage < firstPage)
        {
            throw new InvalidDataException("Faixa de páginas inválida na classificação.");
        }

        return new ClassifiedPart(type, new PageRange(firstPage, lastPage));
    }
}
