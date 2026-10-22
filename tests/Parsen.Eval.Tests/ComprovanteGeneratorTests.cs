using System.Text.Json.Nodes;
using Parsen.Application.Pipeline;
using Parsen.Domain.Identifiers;
using Parsen.Eval;
using Parsen.Eval.Synthetic;
using Parsen.Schemas;

namespace Parsen.Eval.Tests;

public sealed class ComprovanteGeneratorTests
{
    private static readonly List<(OcrDocument Ocr, Gabarito Gabarito)> Samples = Generate(seed: 5, count: 40);

    // Campo novo no schema sem gabarito ficaria fora da avaliação sem ninguém perceber
    [Fact]
    public void Gabarito_HasExactlyTheSchemaFields()
    {
        var schemaFields = JsonNode.Parse(ExtractionSchemas.ComprovanteResidencia.Schema)!["properties"]!.AsObject()
            .Select(p => p.Key).Where(k => k != "alertas").Order(StringComparer.Ordinal);

        Assert.All(Samples, s => Assert.Equal(schemaFields, s.Gabarito.Fields.Keys.Order(StringComparer.Ordinal)));
    }

    [Fact]
    public void SameSeed_GeneratesTheSameDocuments()
    {
        Assert.Equal(Generate(9, 3).Select(s => s.Gabarito.ToJson()), Generate(9, 3).Select(s => s.Gabarito.ToJson()));
    }

    [Fact]
    public void TaxIds_HaveValidCheckDigits_AndCepHasEightDigits()
    {
        Assert.All(Samples, s =>
        {
            if (s.Gabarito.Fields["cnpj_emissor"].Text is { } cnpj)
            {
                Assert.True(Cnpj.IsValid(cnpj));
            }

            Assert.Matches("^[0-9]{8}$", s.Gabarito.Fields["cep"].Text!);
            if (s.Gabarito.Fields["cpf_titular"].Text is { } cpf)
            {
                Assert.True(Cpf.IsValid(cpf));
            }
        });
    }

    [Fact]
    public void Variations_AppearAcrossTheSample()
    {
        Assert.Equal(["agua", "energia", "gas", "telecom"], Samples.Select(s => s.Gabarito.Fields["subtipo"].Text!).Distinct().Order(StringComparer.Ordinal));
        Assert.Contains(Samples, s => s.Gabarito.Fields["cpf_titular"].IsAbsent && s.Ocr.Pages[0].Lines.Any(l => l.Text.Contains("***", StringComparison.Ordinal)));
        Assert.Contains(Samples, s => !s.Gabarito.Fields["cpf_titular"].IsAbsent);
        Assert.Contains(Samples, s => !s.Gabarito.Fields["endereco_entrega"].IsAbsent);
        Assert.Contains(Samples, s => s.Gabarito.Fields["complemento"].IsAbsent);
        Assert.Contains(Samples, s => s.Gabarito.Fields["cnpj_emissor"].IsAbsent && s.Ocr.Pages[0].Lines.Any(l => l.Text.StartsWith("CNPJ:", StringComparison.Ordinal) && l.Text.Contains("***", StringComparison.Ordinal)));
    }

    // Os valores esperados estão impressos no OCR (o endereço de entrega numa linha só): a evidência é localizável
    [Fact]
    public void ExpectedValues_ArePrintedInTheOcr()
    {
        Assert.All(Samples, s =>
        {
            var text = string.Join('\n', s.Ocr.Pages[0].Lines.Select(l => l.Text));
            Assert.Contains(SyntheticOcrPt(s.Gabarito.Fields["valor_total"].Number!.Value), text, StringComparison.Ordinal);
            Assert.Contains(s.Gabarito.Fields["titular"].Text!, text, StringComparison.Ordinal);
            if (s.Gabarito.Fields["endereco_entrega"].Text is { } delivery)
            {
                Assert.Contains(s.Ocr.Pages[0].Lines, l => l.Text == delivery);
            }
        });
    }

    private static List<(OcrDocument Ocr, Gabarito Gabarito)> Generate(int seed, int count)
    {
        var generator = new ComprovanteGenerator(seed);
        return [.. Enumerable.Range(0, count).Select(_ => generator.Generate())];
    }

    private static string SyntheticOcrPt(decimal value) =>
        value.ToString("#,##0.00", System.Globalization.CultureInfo.InvariantCulture)
            .Replace(",", "_", StringComparison.Ordinal).Replace(".", ",", StringComparison.Ordinal).Replace("_", ".", StringComparison.Ordinal);
}
