using System.Text;
using System.Text.Json;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime.Documents;
using Parsen.Application.Pipeline;
using Parsen.Schemas;

namespace Parsen.Infrastructure.Extraction;

/// <param name="ModelId">Inference profile do Bedrock (ex.: global.anthropic.claude-haiku-4-5-20251001-v1:0); vem da configuração, nunca fixo.</param>
/// <param name="MaxTokens">Teto da resposta; um holerite usa ~1.000 tokens de saída.</param>
public sealed record BedrockExtractionSettings(string ModelId, int MaxTokens = 4096);

/// <summary>Extração com o Claude via Bedrock Converse: o schema é uma ferramenta que o modelo é obrigado a chamar.</summary>
public sealed class BedrockExtractionEngine(IAmazonBedrockRuntime bedrock, BedrockExtractionSettings settings) : IExtractionEngine
{
    public bool Supports(string documentType) => ExtractionSchemas.ForDocumentType(documentType) is not null;

    public async Task<ModelExtraction> ExtractAsync(string documentType, OcrDocument ocr, CancellationToken cancellationToken)
    {
        var definition = ExtractionSchemas.ForDocumentType(documentType)
            ?? throw new ArgumentOutOfRangeException(nameof(documentType), documentType, "Tipo de documento sem schema de extração.");

        var response = await bedrock.ConverseAsync(ConverseExtraction.BuildRequest(definition, settings, ocr), cancellationToken);
        return ConverseExtraction.ReadResponse(definition, settings.ModelId, response);
    }
}

/// <summary>Montagem da requisição e leitura da resposta do Converse, sem rede (testável).</summary>
public static class ConverseExtraction
{
    /// <summary>Lista de observações do modelo; todo outro array do schema é uma lista de itens (ex.: rubricas).</summary>
    private const string AlertsProperty = "alertas";

    /// <summary>Requisição de extração: só as primeiras MaxPages páginas do documento, quando o tipo define o limite.</summary>
    public static ConverseRequest BuildRequest(ExtractionDefinition definition, BedrockExtractionSettings settings, OcrDocument ocr)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(ocr);

        var pages = definition.MaxPages is { } max ? new OcrDocument([.. ocr.Pages.Take(max)]) : ocr;
        return BuildRequest(definition, settings, FormatOcr(pages));
    }

    /// <summary>Requisição com o schema como ferramenta obrigatória e o texto do documento na mensagem.</summary>
    public static ConverseRequest BuildRequest(ExtractionDefinition definition, BedrockExtractionSettings settings, string text)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(settings);

        // Ordem do cache: ferramentas → system. O cachePoint depois do prompt guarda os dois, que são fixos
        // por versão; o OCR, que muda a cada documento, vai na mensagem, depois do ponto de cache
        return new ConverseRequest
        {
            ModelId = settings.ModelId,
            System =
            [
                new SystemContentBlock { Text = definition.SystemPrompt },
                new SystemContentBlock { CachePoint = new CachePointBlock { Type = CachePointType.Default } },
            ],
            Messages =
            [
                new Message
                {
                    Role = ConversationRole.User,
                    Content = [new ContentBlock { Text = text }],
                },
            ],
            InferenceConfig = new InferenceConfiguration { MaxTokens = settings.MaxTokens },
            ToolConfig = new ToolConfiguration
            {
                Tools =
                [
                    new Tool
                    {
                        ToolSpec = new ToolSpecification
                        {
                            Name = definition.ToolName,
                            Description = $"Registra os dados extraídos do documento ({definition.DocumentType}).",
                            InputSchema = new ToolInputSchema { Json = ToDocument(definition.Schema) },
                        },
                    },
                ],
                ToolChoice = new ToolChoice { Tool = new SpecificToolChoice { Name = definition.ToolName } },
            },
        };
    }

    /// <summary>Texto do OCR no formato que o prompt descreve: um bloco por página, linhas na ordem de leitura.</summary>
    public static string FormatOcr(OcrDocument ocr)
    {
        ArgumentNullException.ThrowIfNull(ocr);
        var text = new StringBuilder();
        foreach (var page in ocr.Pages)
        {
            text.Append("=== PÁGINA ").Append(page.Page).Append(" ===\n");
            foreach (var line in page.Lines)
            {
                text.Append(line.Text).Append('\n');
            }
        }

        return text.ToString();
    }

    /// <exception cref="DocumentRejectedException">Resposta cortada ou recusada: repetir daria o mesmo resultado.</exception>
    /// <exception cref="InvalidDataException">Saída malformada: erro técnico, o Step Functions tenta de novo.</exception>
    public static ModelExtraction ReadResponse(ExtractionDefinition definition, string modelId, ConverseResponse response)
    {
        using var json = ReadToolInput(definition, response);
        return Parse(definition.Id, modelId, json.RootElement, ToUsage(response.Usage));
    }

    /// <summary>JSON que o modelo passou para a ferramenta, depois de conferir por que a resposta parou.</summary>
    /// <exception cref="DocumentRejectedException">Resposta cortada ou recusada: repetir daria o mesmo resultado.</exception>
    /// <exception cref="InvalidDataException">Sem a chamada da ferramenta: erro técnico, o Step Functions tenta de novo.</exception>
    public static JsonDocument ReadToolInput(ExtractionDefinition definition, ConverseResponse response)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(response);

        var stopReason = response.StopReason?.Value;
        if (stopReason == StopReason.Max_tokens.Value)
        {
            throw new DocumentRejectedException("EXTRACAO_INCOMPLETA", "A extração passou do limite de tokens da resposta.");
        }

        // "refusal" não tem constante no SDK: chega como texto
        if (stopReason == "refusal" || stopReason == StopReason.Content_filtered.Value || stopReason == StopReason.Guardrail_intervened.Value)
        {
            throw new DocumentRejectedException("EXTRACAO_RECUSADA", $"O modelo recusou a extração ({stopReason}).");
        }

        var toolUse = response.Output?.Message?.Content?.FirstOrDefault(c => c.ToolUse?.Name == definition.ToolName)?.ToolUse
            ?? throw new InvalidDataException($"Resposta sem a chamada da ferramenta {definition.ToolName} (stopReason={stopReason}).");

        return JsonDocument.Parse(ToJson(toolUse.Input));
    }

    // Os campos do SDK são int? (ausentes em respostas sem cache, por exemplo)
    public static ModelUsage ToUsage(TokenUsage? usage) => usage is null
        ? ModelUsage.None
        : new ModelUsage(usage.InputTokens ?? 0, usage.OutputTokens ?? 0, usage.CacheReadInputTokens ?? 0, usage.CacheWriteInputTokens ?? 0);

    /// <summary>Converte o JSON da ferramenta no formato da Parsen. Campos ausentes vêm como null.</summary>
    public static ModelExtraction Parse(string schemaId, string modelId, JsonElement input, ModelUsage usage)
    {
        if (input.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("A entrada da ferramenta não é um objeto JSON.");
        }

        var fields = new Dictionary<string, ModelField?>();
        var lists = new Dictionary<string, IReadOnlyList<ModelItem>>();
        IReadOnlyList<string> alerts = [];

        foreach (var property in input.EnumerateObject())
        {
            var value = property.Value;
            if (property.Name == AlertsProperty)
            {
                alerts = value.ValueKind == JsonValueKind.Array ? [.. value.EnumerateArray().Select(a => a.GetString() ?? "")] : [];
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                lists[property.Name] = [.. value.EnumerateArray().Select(ParseItem)];
            }
            else if (value.ValueKind == JsonValueKind.Null)
            {
                fields[property.Name] = null;
            }
            else
            {
                fields[property.Name] = new ModelField(
                    ToFieldValue(Required(value, "valor")) ?? throw new InvalidDataException($"Campo {property.Name} sem valor."),
                    Page(value),
                    SourceText(value));
            }
        }

        return new ModelExtraction(schemaId, modelId, fields, lists, alerts, usage);
    }

    private static ModelItem ParseItem(JsonElement item)
    {
        var attributes = item.EnumerateObject()
            .Where(p => p.Name is not ("pagina" or "trecho_fonte"))
            .ToDictionary(p => p.Name, p => ToFieldValue(p.Value));

        return new ModelItem(attributes, Page(item), SourceText(item));
    }

    // Número fica como o texto exato do JSON (ex.: 4870.35): vira decimal só no domínio, sem passar por double
    private static FieldValue? ToFieldValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.Number => FieldValue.Number(value.GetRawText()),
        JsonValueKind.String => FieldValue.Text(value.GetString()!),
        _ => throw new InvalidDataException($"Valor de tipo inesperado: {value.ValueKind}."),
    };

    private static int Page(JsonElement element) =>
        Required(element, "pagina").TryGetInt32(out var page) && page >= 1
            ? page
            : throw new InvalidDataException("Página inválida na saída do modelo.");

    private static string SourceText(JsonElement element) =>
        Required(element, "trecho_fonte").GetString() is { Length: > 0 } text
            ? text
            : throw new InvalidDataException("trecho_fonte vazio na saída do modelo.");

    private static JsonElement Required(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value
            : throw new InvalidDataException($"Saída do modelo sem '{name}'.");

    /// <summary>JSON → Document do SDK, campo a campo e sem reflexão (compatível com Native AOT).</summary>
    public static Document ToDocument(string json)
    {
        using var parsed = JsonDocument.Parse(json);
        return ToDocument(parsed.RootElement);
    }

    public static byte[] ToJson(Document document)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            Write(writer, document);
        }

        return buffer.ToArray();
    }

    private static Document ToDocument(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => new Document(element.EnumerateObject().ToDictionary(p => p.Name, p => ToDocument(p.Value))),
        JsonValueKind.Array => new Document(element.EnumerateArray().Select(ToDocument).ToList()),
        JsonValueKind.String => new Document(element.GetString()),
        JsonValueKind.Number when element.TryGetInt64(out var integer) => new Document(integer),
        JsonValueKind.Number => new Document(element.GetDouble()),
        JsonValueKind.True => new Document(true),
        JsonValueKind.False => new Document(false),
        _ => new Document(),
    };

    private static void Write(Utf8JsonWriter writer, Document document)
    {
        if (document.IsDictionary())
        {
            writer.WriteStartObject();
            foreach (var (name, value) in document.AsDictionary())
            {
                writer.WritePropertyName(name);
                Write(writer, value);
            }

            writer.WriteEndObject();
        }
        else if (document.IsList())
        {
            writer.WriteStartArray();
            foreach (var value in document.AsList())
            {
                Write(writer, value);
            }

            writer.WriteEndArray();
        }
        else if (document.IsString())
        {
            writer.WriteStringValue(document.AsString());
        }
        else if (document.IsInt())
        {
            writer.WriteNumberValue(document.AsInt());
        }
        else if (document.IsLong())
        {
            writer.WriteNumberValue(document.AsLong());
        }
        else if (document.IsDouble())
        {
            // Menor representação que volta ao mesmo double: 4870.35 sai como 4870.35
            writer.WriteNumberValue(document.AsDouble());
        }
        else if (document.IsBool())
        {
            writer.WriteBooleanValue(document.AsBool());
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
