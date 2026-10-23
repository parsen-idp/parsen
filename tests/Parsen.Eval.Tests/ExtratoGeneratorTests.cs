using System.Text.Json.Nodes;
using Parsen.Application.Pipeline;
using Parsen.Domain.Identifiers;
using Parsen.Eval;
using Parsen.Eval.Synthetic;
using Parsen.Schemas;

namespace Parsen.Eval.Tests;

public sealed class ExtratoGeneratorTests
{
    private static readonly List<(OcrDocument Ocr, Gabarito Gabarito)> Samples = Generate(seed: 13, count: 40);

    [Fact]
    public void Gabarito_HasExactlyTheSchemaFields()
    {
        var schema = JsonNode.Parse(ExtractionSchemas.ExtratoBancario.Schema)!["properties"]!.AsObject().Select(p => p.Key).ToList();
        var scalarFields = schema.Where(k => k is not ("alertas" or "creditos")).Order(StringComparer.Ordinal);

        Assert.All(Samples, s =>
        {
            Assert.Equal(scalarFields, s.Gabarito.Fields.Keys.Order(StringComparer.Ordinal));
            Assert.Equal(["creditos"], s.Gabarito.Lists.Keys);
        });
    }

    [Fact]
    public void SameSeed_GeneratesTheSameDocuments()
    {
        Assert.Equal(Generate(4, 3).Select(s => s.Gabarito.ToJson()), Generate(4, 3).Select(s => s.Gabarito.ToJson()));
    }

    // O saldo final do gabarito fecha com o inicial mais os créditos menos os débitos impressos
    [Fact]
    public void FinalBalance_MatchesTheEntries()
    {
        Assert.All(Samples, s =>
        {
            var credits = s.Gabarito.Lists["creditos"].Sum(c => c["valor"].Number!.Value);
            var debits = s.Ocr.Pages.SelectMany(p => p.Lines).Select(l => l.Text)
                .Where(t => !t.StartsWith("SALDO", StringComparison.Ordinal) && (t.EndsWith(" D", StringComparison.Ordinal) || t.Contains("  -", StringComparison.Ordinal)))
                .Sum(t => decimal.Parse(t.Split("  ")[^1].TrimStart('-').Replace(" D", "", StringComparison.Ordinal).Replace(".", "", StringComparison.Ordinal).Replace(",", ".", StringComparison.Ordinal), System.Globalization.CultureInfo.InvariantCulture));

            Assert.Equal(s.Gabarito.Fields["saldo_final"].Number, s.Gabarito.Fields["saldo_inicial"].Number + credits - debits);
        });
    }

    [Fact]
    public void Variations_AppearAcrossTheSample()
    {
        var lines = Samples.SelectMany(s => s.Ocr.Pages.SelectMany(p => p.Lines)).Select(l => l.Text).ToList();

        Assert.Contains(lines, l => l.EndsWith(" D", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("  -", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("SALDO DO DIA", StringComparison.Ordinal));
        Assert.Contains(Samples, s => s.Ocr.Pages.Count > 1);
        Assert.Contains(Samples, s => s.Gabarito.Fields["saldo_inicial"].Number < 0 || s.Gabarito.Fields["saldo_final"].Number < 0);
        Assert.Contains(Samples, s => s.Gabarito.Lists["creditos"].GroupBy(c => (c["data"].Text, c["valor"].Number)).Any(g => g.Count() > 1));
        Assert.All(Samples, s => Assert.True(Cnpj.IsValid(s.Gabarito.Fields["cnpj_instituicao"].Text!)));
    }

    [Fact]
    public void EveryPage_HasEntries()
    {
        Assert.All(Samples.SelectMany(s => s.Ocr.Pages), p => Assert.Contains(p.Lines, l => l.Text.Length > 5 && char.IsAsciiDigit(l.Text[0])));
    }

    private static List<(OcrDocument Ocr, Gabarito Gabarito)> Generate(int seed, int count)
    {
        var generator = new ExtratoGenerator(seed);
        return [.. Enumerable.Range(0, count).Select(_ => generator.Generate())];
    }
}
