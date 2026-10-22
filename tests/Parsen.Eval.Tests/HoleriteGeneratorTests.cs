using System.Globalization;
using Parsen.Application.Pipeline;
using Parsen.Domain.Identifiers;
using Parsen.Eval;
using Parsen.Eval.Synthetic;

namespace Parsen.Eval.Tests;

public sealed class HoleriteGeneratorTests
{
    private static readonly List<(OcrDocument Ocr, Gabarito Gabarito)> Samples = Generate(seed: 7, count: 40);

    [Fact]
    public void SameSeed_GeneratesTheSameDocuments()
    {
        var first = Generate(seed: 42, count: 3);
        var second = Generate(seed: 42, count: 3);

        Assert.Equal(first.Select(s => s.Gabarito.ToJson()), second.Select(s => s.Gabarito.ToJson()));
    }

    [Fact]
    public void Totals_AddUp()
    {
        foreach (var (_, gabarito) in Samples)
        {
            var itens = gabarito.Lists["itens"];
            decimal Sum(string tipo) => itens.Where(i => i["tipo"].Text == tipo).Sum(i => i["valor"].Number!.Value);

            Assert.Equal(gabarito.Fields["total_vencimentos"].Number, Sum("vencimento"));
            Assert.Equal(gabarito.Fields["total_descontos"].Number, Sum("desconto"));
            Assert.Equal(gabarito.Fields["salario_liquido"].Number, Sum("vencimento") - Sum("desconto"));
        }
    }

    [Fact]
    public void TaxIds_HaveValidCheckDigits()
    {
        foreach (var (_, gabarito) in Samples)
        {
            Assert.True(Cnpj.IsValid(gabarito.Fields["cnpj_empregador"].Text!));
            if (gabarito.Fields["cpf"].Text is { } cpf)
            {
                Assert.True(Cpf.IsValid(cpf));
            }
        }
    }

    [Fact]
    public void Variations_AppearAcrossTheSample()
    {
        Assert.Contains(Samples, s => s.Gabarito.Fields["cpf"].IsAbsent);
        Assert.Contains(Samples, s => !s.Gabarito.Fields["cpf"].IsAbsent);
        Assert.Contains(Samples, s => s.Gabarito.Fields["data_admissao"].IsAbsent);
        Assert.Contains(Samples, s => s.Gabarito.Fields["cnpj_empregador"].Text!.Any(char.IsAsciiLetter));
        Assert.Contains(Samples, s => s.Ocr.Pages[0].Lines.Count(l => l.Text == "DEMONSTRATIVO DE PAGAMENTO DE SALARIO") == 2);
    }

    // Todo valor monetário do gabarito está impresso no OCR (em formato brasileiro): a evidência é localizável
    [Fact]
    public void MoneyValues_ArePrintedInTheOcr()
    {
        foreach (var (ocr, gabarito) in Samples)
        {
            var texts = ocr.Pages[0].Lines.Select(l => l.Text).ToHashSet(StringComparer.Ordinal);
            foreach (var field in new[] { "salario_base", "total_vencimentos", "total_descontos", "salario_liquido" })
            {
                Assert.Contains(Pt(gabarito.Fields[field].Number!.Value), texts);
            }
        }
    }

    [Fact]
    public void WordBoxes_StayInsideThePage()
    {
        var boxes = Samples.SelectMany(s => s.Ocr.Pages[0].Lines.SelectMany(l => l.Words)).Select(w => w.Bbox);

        Assert.All(boxes, b => Assert.True(b[0] >= 0 && b[1] >= 0 && b[0] + b[2] <= 1 && b[1] + b[3] <= 1));
    }

    private static List<(OcrDocument Ocr, Gabarito Gabarito)> Generate(int seed, int count)
    {
        var generator = new HoleriteGenerator(seed);
        return [.. Enumerable.Range(0, count).Select(_ => generator.Generate())];
    }

    private static string Pt(decimal value) =>
        value.ToString("#,##0.00", CultureInfo.InvariantCulture).Replace(",", "_", StringComparison.Ordinal).Replace(".", ",", StringComparison.Ordinal).Replace("_", ".", StringComparison.Ordinal);
}
