using System.Text.Json.Serialization;

namespace Parsen.Workers.Common;

// Contrato entre as etapas da máquina de estados. Só IDs e contagens: nenhum dado pessoal
// passa pelo Step Functions, que guarda o histórico de entradas e saídas de cada execução (regra 3).

/// <summary>Um documento do dossiê: item do estado Map, entrada do Ingest (e, depois, do OCR e do Extract).</summary>
public sealed record DocumentTask(string TenantId, string DossierId, string DocumentId);

public sealed record IngestResult(string DocumentId, int PageCount);

/// <summary>Entrada do Finalize. Error vem do Catch do Step Functions quando alguma etapa falhou de vez.</summary>
public sealed record FinalizeInput(string TenantId, string DossierId, string Outcome, PipelineError? Error);

/// <summary>Formato que o Step Functions grava no Catch: o tipo do erro e a mensagem.</summary>
public sealed record PipelineError(
    [property: JsonPropertyName("Error")] string? Error,
    [property: JsonPropertyName("Cause")] string? Cause);

public sealed record FinalizeResult(string DossierId, string Status);

public static class FinalizeOutcomes
{
    public const string Failed = "failed";
    public const string NeedsReview = "needs_review";
}

/// <summary>Serialização gerada em tempo de compilação: sem reflexão, compatível com Native AOT.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(DocumentTask))]
[JsonSerializable(typeof(IngestResult))]
[JsonSerializable(typeof(FinalizeInput))]
[JsonSerializable(typeof(FinalizeResult))]
public sealed partial class WorkersJsonContext : JsonSerializerContext;
