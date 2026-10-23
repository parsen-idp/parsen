using System.Globalization;
using Parsen.Application.Pipeline;
using Parsen.Domain.Dossiers;

namespace Parsen.Eval;

public enum FieldOutcome
{
    /// <summary>Valor igual ao gabarito (ou os dois ausentes).</summary>
    Correct,

    /// <summary>Valor diferente do gabarito.</summary>
    Wrong,

    /// <summary>O documento tem o campo, mas o modelo devolveu null.</summary>
    Missed,

    /// <summary>O documento não tem o campo, mas o modelo devolveu um valor: o erro mais grave ("nunca invente").</summary>
    Invented,
}

/// <param name="Located">O trecho foi achado no OCR (confiança maior que 0); null quando não há valor.</param>
public sealed record FieldScore(string Field, FieldOutcome Outcome, string Expected, string? Actual, bool? Located);

/// <param name="Matched">Linhas esperadas encontradas na extração (mesmo tipo e valor).</param>
/// <param name="Extra">Linhas extraídas que não estão no gabarito.</param>
public sealed record ListScore(string List, int Expected, int Matched, int Extra);

public sealed record CaseScore(IReadOnlyList<FieldScore> Fields, IReadOnlyList<ListScore> Lists);

/// <param name="Exact">Separação e tipos iguais ao gabarito, documento por documento.</param>
/// <param name="Matched">Documentos com tipo e faixa de páginas exatos.</param>
/// <param name="PagesWithRightType">Páginas cujo tipo atribuído é o do gabarito (a medida mais fina).</param>
public sealed record ClassificationScore(
    bool Exact, int Expected, int Matched, int Extra, int Pages, int PagesWithRightType, string ExpectedSummary, string ActualSummary);

/// <summary>Compara a classificação com o gabarito (lista "documentos" com tipo, pagina_inicial e pagina_final).</summary>
public static class ClassificationComparer
{
    public static ClassificationScore Compare(Gabarito expected, ClassificationOutput actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        var expectedParts = expected.Lists["documentos"]
            .Select(d => (Type: d["tipo"].Text!, First: (int)d["pagina_inicial"].Number!.Value, Last: (int)d["pagina_final"].Number!.Value))
            .ToList();
        var actualParts = actual.Parts.Select(p => (Type: DocumentTypeCodes.ToCode(p.Type), p.Pages.First, p.Pages.Last)).ToList();

        var matched = expectedParts.Count(actualParts.Contains);
        var pages = expectedParts.Count == 0 ? 0 : expectedParts.Max(p => p.Last);
        var rightPages = Enumerable.Range(1, pages).Count(page => TypeAt(expectedParts, page) == TypeAt(actualParts, page));

        return new ClassificationScore(
            expectedParts.SequenceEqual(actualParts),
            expectedParts.Count,
            matched,
            actualParts.Count - actualParts.Count(expectedParts.Contains),
            pages,
            rightPages,
            Summary(expectedParts),
            Summary(actualParts));
    }

    private static string? TypeAt(List<(string Type, int First, int Last)> parts, int page) =>
        parts.FirstOrDefault(p => page >= p.First && page <= p.Last).Type;

    private static string Summary(List<(string Type, int First, int Last)> parts) =>
        string.Join(" + ", parts.Select(p => p.First == p.Last ? $"{p.Type} {p.First}" : $"{p.Type} {p.First}–{p.Last}"));
}

/// <summary>Compara uma extração com o gabarito: números como decimal exato; textos sem diferenciar maiúsculas e espaços.</summary>
public static class Comparer
{
    /// <summary>
    /// Atributos que identificam uma linha de lista, quando o gabarito os tem: tipo (rubrica do holerite), data (crédito
    /// do extrato) e valor. A descrição fica de fora: varia muito entre layouts.
    /// </summary>
    private static readonly string[] ItemKey = ["tipo", "data", "valor"];

    public static CaseScore Compare(Gabarito expected, ExtractionResult actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        var extracted = actual.Fields.ToDictionary(f => f.Name);
        var fields = expected.Fields.Select(e =>
        {
            var field = extracted.GetValueOrDefault(e.Key);
            var value = field?.Value;
            var outcome = (e.Value.IsAbsent, value is null) switch
            {
                (true, true) => FieldOutcome.Correct,
                (true, false) => FieldOutcome.Invented,
                (false, true) => FieldOutcome.Missed,
                _ => AreEqual(e.Value, value!) ? FieldOutcome.Correct : FieldOutcome.Wrong,
            };

            return new FieldScore(e.Key, outcome, e.Value.ToString(), value?.Raw, value is null ? null : field!.Confidence > 0);
        }).ToList();

        var lists = expected.Lists.Select(l => CompareList(l.Key, l.Value, actual.Lists.GetValueOrDefault(l.Key) ?? [])).ToList();
        return new CaseScore(fields, lists);
    }

    public static bool AreEqual(ExpectedValue expected, FieldValue actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        if (expected.Number is { } number)
        {
            return decimal.TryParse(actual.Raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) && parsed == number;
        }

        return string.Equals(NormalizeText(expected.Text!), NormalizeText(actual.Raw), StringComparison.Ordinal);
    }

    private static ListScore CompareList(string name, IReadOnlyList<IReadOnlyDictionary<string, ExpectedValue>> expected, IReadOnlyList<ExtractedItem> actual)
    {
        // Cada linha extraída casa com no máximo uma linha esperada (rubricas iguais repetidas contam separado)
        var available = actual.ToList();
        var matched = 0;
        foreach (var item in expected)
        {
            var match = available.FirstOrDefault(a => ItemKey.Where(item.ContainsKey).All(key => SameAttribute(item.GetValueOrDefault(key), a.Attributes.GetValueOrDefault(key))));
            if (match is not null)
            {
                available.Remove(match);
                matched++;
            }
        }

        return new ListScore(name, expected.Count, matched, available.Count);
    }

    private static bool SameAttribute(ExpectedValue? expected, FieldValue? actual) =>
        expected is null || expected.IsAbsent ? actual is null : actual is not null && AreEqual(expected, actual);

    private static string NormalizeText(string text) =>
        string.Join(' ', text.ToUpperInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
