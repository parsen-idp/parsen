using System.Globalization;
using System.Text;
using Parsen.Application.Pipeline;

namespace Parsen.Eval;

/// <summary>Preço por milhão de tokens (US$). Valores do Bedrock, endpoint Global (API de preços da AWS, 20/10/2026).</summary>
public sealed record ModelPrice(decimal Input, decimal Output, decimal CacheRead, decimal CacheWrite)
{
    private static readonly Dictionary<string, ModelPrice> Known = new(StringComparer.Ordinal)
    {
        ["global.anthropic.claude-haiku-4-5-20251001-v1:0"] = new(1.00m, 5.00m, 0.10m, 1.25m),
        ["global.anthropic.claude-haiku-5-5"] = new(0.10m, 0.50m, 0.01m, 0.125m),
    };

    public static ModelPrice For(string modelId) =>
        Known.TryGetValue(modelId, out var price)
            ? price
            : throw new ArgumentException($"Sem preço conhecido para {modelId}; adicione em ModelPrice.", nameof(modelId));

    public decimal Cost(ModelUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        return ((usage.InputTokens * Input) + (usage.OutputTokens * Output) + (usage.CacheReadTokens * CacheRead) + (usage.CacheWriteTokens * CacheWrite)) / 1_000_000m;
    }
}

/// <param name="Synthetic">Caso sintético (fixtures/): o relatório pode mostrar os valores. Documento próprio: só nomes de campo.</param>
public sealed record CaseResult(string Name, bool Synthetic, CaseScore? Score, ModelUsage Usage, decimal Cost, string? Error);

/// <summary>Relatório em Markdown: totais, acerto por campo, casos e falhas.</summary>
public static class Report
{
    public static string Render(string schemaId, string modelId, IReadOnlyList<CaseResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        var scored = results.Where(r => r.Score is not null).ToList();
        var fields = scored.SelectMany(r => r.Score!.Fields).ToList();
        var lists = scored.SelectMany(r => r.Score!.Lists).ToList();
        var withValue = fields.Where(f => f.Located is not null).ToList();
        var md = new StringBuilder();

        md.AppendLine(CultureInfo.InvariantCulture, $"# Parsen.Eval — {schemaId}");
        md.AppendLine();
        md.AppendLine(CultureInfo.InvariantCulture, $"Modelo: `{modelId}` · casos: {results.Count} ({results.Count(r => r.Synthetic)} sintéticos) · erros: {results.Count(r => r.Error is not null)}");
        md.AppendLine();
        md.AppendLine("| Métrica | Valor |");
        md.AppendLine("| --- | --- |");
        md.AppendLine(CultureInfo.InvariantCulture, $"| Acerto por campo | {Percent(fields.Count(f => f.Outcome == FieldOutcome.Correct), fields.Count)} |");
        md.AppendLine(CultureInfo.InvariantCulture, $"| Valores inventados | {fields.Count(f => f.Outcome == FieldOutcome.Invented)} |");
        md.AppendLine(CultureInfo.InvariantCulture, $"| Campos não extraídos | {fields.Count(f => f.Outcome == FieldOutcome.Missed)} |");
        md.AppendLine(CultureInfo.InvariantCulture, $"| Trecho achado no OCR | {Percent(withValue.Count(f => f.Located == true), withValue.Count)} |");
        md.AppendLine(CultureInfo.InvariantCulture, $"| Linhas de lista encontradas | {Percent(lists.Sum(l => l.Matched), lists.Sum(l => l.Expected))} ({lists.Sum(l => l.Extra)} a mais) |");
        md.AppendLine(CultureInfo.InvariantCulture, $"| Tokens (entrada / saída / cache lido) | {results.Sum(r => r.Usage.InputTokens)} / {results.Sum(r => r.Usage.OutputTokens)} / {results.Sum(r => r.Usage.CacheReadTokens)} |");
        md.AppendLine(CultureInfo.InvariantCulture, $"| Custo total / por documento | US$ {results.Sum(r => r.Cost):0.0000} / US$ {(results.Count == 0 ? 0 : results.Average(r => r.Cost)):0.0000} |");

        md.AppendLine();
        md.AppendLine("## Acerto por campo");
        md.AppendLine();
        md.AppendLine("| Campo | Acerto | Errado | Não extraído | Inventado |");
        md.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var group in fields.GroupBy(f => f.Field).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            md.AppendLine(CultureInfo.InvariantCulture,
                $"| {group.Key} | {Percent(group.Count(f => f.Outcome == FieldOutcome.Correct), group.Count())} | {group.Count(f => f.Outcome == FieldOutcome.Wrong)} | {group.Count(f => f.Outcome == FieldOutcome.Missed)} | {group.Count(f => f.Outcome == FieldOutcome.Invented)} |");
        }

        md.AppendLine();
        md.AppendLine("## Casos");
        md.AppendLine();
        md.AppendLine("| Caso | Campos certos | Listas | Tokens (entrada / saída) | Custo | Falhas |");
        md.AppendLine("| --- | --- | --- | --- | --- | --- |");
        foreach (var result in results)
        {
            if (result.Score is null)
            {
                md.AppendLine(CultureInfo.InvariantCulture, $"| {result.Name} | — | — | — | — | erro: {result.Error} |");
                continue;
            }

            var score = result.Score;
            var failures = score.Fields.Where(f => f.Outcome != FieldOutcome.Correct).Select(f => Describe(f, result.Synthetic));
            md.AppendLine(CultureInfo.InvariantCulture,
                $"| {result.Name} | {score.Fields.Count(f => f.Outcome == FieldOutcome.Correct)}/{score.Fields.Count} | " +
                $"{string.Join(", ", score.Lists.Select(l => $"{l.List} {l.Matched}/{l.Expected}{(l.Extra > 0 ? $" (+{l.Extra})" : "")}"))} | " +
                $"{result.Usage.InputTokens} / {result.Usage.OutputTokens} | US$ {result.Cost:0.0000} | {string.Join("; ", failures)} |");
        }

        return md.ToString();
    }

    // Documento próprio: só o nome do campo e o tipo do erro, nunca o valor (dados pessoais, regra 3)
    private static string Describe(FieldScore field, bool synthetic) => synthetic
        ? $"{field.Field} {Label(field.Outcome)} (esperado `{field.Expected}`, veio `{field.Actual ?? "null"}`)"
        : $"{field.Field} {Label(field.Outcome)}";

    private static string Label(FieldOutcome outcome) => outcome switch
    {
        FieldOutcome.Wrong => "errado",
        FieldOutcome.Missed => "não extraído",
        FieldOutcome.Invented => "INVENTADO",
        _ => "ok",
    };

    private static string Percent(int part, int total) =>
        total == 0 ? "—" : string.Create(CultureInfo.InvariantCulture, $"{100m * part / total:0.0}% ({part}/{total})");
}
