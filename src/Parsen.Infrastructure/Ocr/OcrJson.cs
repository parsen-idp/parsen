using System.Text.Json;
using System.Text.Json.Serialization;
using Parsen.Application.Pipeline;

namespace Parsen.Infrastructure.Ocr;

/// <summary>Leitura e gravação do textract.json. JSON gerado em tempo de compilação: compatível com Native AOT.</summary>
public static class OcrJson
{
    public static byte[] Serialize(OcrDocument document) =>
        JsonSerializer.SerializeToUtf8Bytes(document, OcrJsonContext.Default.OcrDocument);

    public static OcrDocument Deserialize(byte[] json) =>
        JsonSerializer.Deserialize(json, OcrJsonContext.Default.OcrDocument)
            ?? throw new InvalidDataException("textract.json vazio.");
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(OcrDocument))]
internal sealed partial class OcrJsonContext : JsonSerializerContext;
