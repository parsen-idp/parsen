using System.Text.Json.Nodes;
using Parsen.Application.Pipeline;
using Parsen.Domain.Identifiers;
using Parsen.Eval;
using Parsen.Eval.Synthetic;
using Parsen.Schemas;

namespace Parsen.Eval.Tests;

public sealed class MatriculaGeneratorTests
{
    private static readonly List<(OcrDocument Ocr, Gabarito Gabarito)> Samples = Generate(seed: 17, count: 40);

    [Fact]
    public void Gabarito_HasExactlyTheSchemaFields()
    {
        var properties = JsonNode.Parse(ExtractionSchemas.MatriculaImovel.Schema)!["properties"]!.AsObject().Select(p => p.Key).ToList();
        var scalarFields = properties.Where(k => k is not ("alertas" or "atos" or "partes")).Order(StringComparer.Ordinal);

        Assert.All(Samples, s =>
        {
            Assert.Equal(scalarFields, s.Gabarito.Fields.Keys.Order(StringComparer.Ordinal));
            Assert.Equal(["atos", "partes"], s.Gabarito.Lists.Keys.Order(StringComparer.Ordinal));
        });
    }

    [Fact]
    public void SameSeed_GeneratesTheSameDocuments()
    {
        Assert.Equal(Generate(8, 3).Select(s => s.Gabarito.ToJson()), Generate(8, 3).Select(s => s.Gabarito.ToJson()));
    }

    // Cancelamento sempre aponta um ato anterior que existe; toda parte pertence a um ato da lista
    [Fact]
    public void ActsAndParties_AreConsistent()
    {
        Assert.All(Samples, s =>
        {
            var acts = s.Gabarito.Lists["atos"].Select(a => a["numero"].Text!).ToList();
            Assert.Equal(acts.Distinct().Count(), acts.Count);
            Assert.All(s.Gabarito.Lists["atos"].Where(a => !a["cancela"].IsAbsent), a =>
                Assert.True(acts.IndexOf(a["cancela"].Text!) is var i && i >= 0 && i < acts.IndexOf(a["numero"].Text!)));
            Assert.All(s.Gabarito.Lists["partes"], p => Assert.Contains(p["ato"].Text!, acts));
            Assert.All(s.Gabarito.Lists["partes"].Where(p => p["cpf_cnpj"].Text is { Length: 11 }), p => Assert.True(Cpf.IsValid(p["cpf_cnpj"].Text!)));
        });
    }

    // Proprietário da abertura não é parte de ato: o nome dele na abertura não vira item de "partes" por si só
    [Fact]
    public void OpeningOwner_IsOnlyATransmitterOfR1()
    {
        Assert.All(Samples, s =>
        {
            var opener = s.Ocr.Pages[0].Lines.First(l => l.Text.StartsWith("PROPRIETÁRIO:", StringComparison.Ordinal)).Text[14..].Split(',')[0];
            Assert.All(s.Gabarito.Lists["partes"].Where(p => p["nome"].Text == opener), p => Assert.Equal("transmitente", p["papel"].Text));
        });
    }

    [Fact]
    public void Variations_AppearAcrossTheSample()
    {
        var types = Samples.SelectMany(s => s.Gabarito.Lists["atos"]).Select(a => a["tipo"].Text).ToHashSet();
        var lines = Samples.SelectMany(s => s.Ocr.Pages.SelectMany(p => p.Lines)).Select(l => l.Text).ToList();

        Assert.Superset(new HashSet<string?> { "compra_venda", "alienacao_fiduciaria", "cancelamento", "penhora", "construcao" }, types);
        Assert.Contains(lines, l => l.StartsWith("AV-", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("Av.", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("Data: ", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains(" de 20", StringComparison.Ordinal));
        Assert.Contains(Samples, s => s.Gabarito.Fields["area_privativa"].IsAbsent);
        Assert.Contains(Samples, s => s.Ocr.Pages.Count > 1);
    }

    // Cada nome de parte está inteiro numa linha do OCR: a evidência é localizável
    [Fact]
    public void PartyNames_ArePrintedOnASingleLine()
    {
        Assert.All(Samples, s =>
        {
            var lines = s.Ocr.Pages.SelectMany(p => p.Lines).Select(l => l.Text).ToList();
            Assert.All(s.Gabarito.Lists["partes"], p => Assert.Contains(lines, l => l.Contains(p["nome"].Text!, StringComparison.Ordinal)));
        });
    }

    private static List<(OcrDocument Ocr, Gabarito Gabarito)> Generate(int seed, int count)
    {
        var generator = new MatriculaGenerator(seed);
        return [.. Enumerable.Range(0, count).Select(_ => generator.Generate())];
    }
}
