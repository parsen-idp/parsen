using System.Globalization;
using Parsen.Domain.Dossiers;

namespace Parsen.Infrastructure.DynamoDb;

/// <summary>
/// Valores gravados na tabela. Mapeamento explícito: renomear um enum no C# não pode invalidar
/// itens já gravados.
/// </summary>
internal static class StoredValues
{
    // Largura fixa (7 casas decimais) mantém a ordenação de texto igual à ordenação de data
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    public static string From(DossierStatus status) => status switch
    {
        DossierStatus.Draft => "draft",
        DossierStatus.Processing => "processing",
        DossierStatus.Ready => "ready",
        DossierStatus.NeedsReview => "needs_review",
        DossierStatus.Approved => "approved",
        DossierStatus.Rejected => "rejected",
        DossierStatus.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static DossierStatus ToDossierStatus(string value) => value switch
    {
        "draft" => DossierStatus.Draft,
        "processing" => DossierStatus.Processing,
        "ready" => DossierStatus.Ready,
        "needs_review" => DossierStatus.NeedsReview,
        "approved" => DossierStatus.Approved,
        "rejected" => DossierStatus.Rejected,
        "failed" => DossierStatus.Failed,
        _ => throw new InvalidDataException($"Status de dossiê desconhecido na tabela: '{value}'."),
    };

    public static string From(AnalysisType type) => type switch
    {
        AnalysisType.Income => "income",
        AnalysisType.Property => "property",
        AnalysisType.IncomeCheck => "income_check",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public static AnalysisType ToAnalysisType(string value) => value switch
    {
        "income" => AnalysisType.Income,
        "property" => AnalysisType.Property,
        "income_check" => AnalysisType.IncomeCheck,
        _ => throw new InvalidDataException($"Tipo de análise desconhecido na tabela: '{value}'."),
    };

    public static string From(DateTimeOffset timestamp) =>
        timestamp.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture);

    public static DateTimeOffset ToTimestamp(string value) =>
        DateTimeOffset.ParseExact(value, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    public static string From(long number) => number.ToString(CultureInfo.InvariantCulture);

    public static long ToLong(string value) => long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
}
