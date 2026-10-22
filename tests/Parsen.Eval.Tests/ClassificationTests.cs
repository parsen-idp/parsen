using Parsen.Application.Pipeline;
using Parsen.Domain.Dossiers;
using Parsen.Eval;
using Parsen.Eval.Synthetic;

namespace Parsen.Eval.Tests;

public sealed class ClassificationTests
{
    private static readonly Gabarito Expected = Gabarito.Parse("""
        {
          "document_type": "classificacao",
          "fields": {},
          "lists": { "documentos": [
            { "tipo": "holerite", "pagina_inicial": 1, "pagina_final": 1 },
            { "tipo": "extrato_bancario", "pagina_inicial": 2, "pagina_final": 4 }
          ] }
        }
        """);

    [Fact]
    public void SameSplitAndTypes_IsExact()
    {
        var score = Compare((DocumentType.Holerite, 1, 1), (DocumentType.ExtratoBancario, 2, 4));

        Assert.True(score.Exact);
        Assert.Equal(2, score.Matched);
        Assert.Equal(0, score.Extra);
        Assert.Equal(4, score.PagesWithRightType);
    }

    // Extrato dividido em dois: os tipos de página estão certos, mas a separação não
    [Fact]
    public void WrongSplit_CountsDocumentsAndPagesSeparately()
    {
        var score = Compare((DocumentType.Holerite, 1, 1), (DocumentType.ExtratoBancario, 2, 2), (DocumentType.ExtratoBancario, 3, 4));

        Assert.False(score.Exact);
        Assert.Equal(1, score.Matched);
        Assert.Equal(2, score.Extra);
        Assert.Equal(4, score.PagesWithRightType);
    }

    [Fact]
    public void WrongType_LosesTheDocumentAndItsPages()
    {
        var score = Compare((DocumentType.Holerite, 1, 1), (DocumentType.Outro, 2, 4));

        Assert.Equal(1, score.Matched);
        Assert.Equal(1, score.PagesWithRightType);
    }

    [Fact]
    public void Bundles_AreDeterministic()
    {
        var first = new BundleGenerator(3);
        var second = new BundleGenerator(3);

        Assert.Equal(first.Generate().Gabarito.ToJson(), second.Generate().Gabarito.ToJson());
    }

    // As faixas do gabarito cobrem as páginas do OCR em ordem, sem buracos nem sobreposição
    [Fact]
    public void Bundles_PartsCoverEveryPageInOrder()
    {
        var generator = new BundleGenerator(11);
        for (var i = 0; i < 30; i++)
        {
            var (ocr, gabarito) = generator.Generate();
            var parts = gabarito.Lists["documentos"];
            var next = 1;
            foreach (var part in parts)
            {
                Assert.Equal(next, (int)part["pagina_inicial"].Number!.Value);
                next = (int)part["pagina_final"].Number!.Value + 1;
                Assert.True(DocumentTypeCodes.TryParse(part["tipo"].Text, out _));
            }

            Assert.Equal(ocr.Pages.Count, next - 1);
            Assert.Equal(Enumerable.Range(1, ocr.Pages.Count), ocr.Pages.Select(p => p.Page));
        }
    }

    // Um extrato com cabeçalho repetido tem o mesmo titular e conta em todas as páginas; se mudassem,
    // o modelo estaria certo em separar (foi o erro do gerador na primeira rodada da classificacao.v1)
    [Fact]
    public void Bundles_ExtratoHeaderIsTheSameOnEveryPage()
    {
        var generator = new BundleGenerator(11);
        for (var i = 0; i < 30; i++)
        {
            var (ocr, gabarito) = generator.Generate();
            foreach (var part in gabarito.Lists["documentos"].Where(p => p["tipo"].Text == "extrato_bancario"))
            {
                var headers = ocr.Pages
                    .Where(p => p.Page >= part["pagina_inicial"].Number && p.Page <= part["pagina_final"].Number)
                    .Where(p => p.Lines.Count > 3 && p.Lines[1].Text == "EXTRATO DE CONTA CORRENTE")
                    .Select(p => $"{p.Lines[2].Text}|{p.Lines[3].Text}")
                    .Distinct()
                    .ToList();

                Assert.Single(headers);
            }
        }
    }

    private static ClassificationScore Compare(params (DocumentType Type, int First, int Last)[] parts) =>
        ClassificationComparer.Compare(Expected, new ClassificationOutput(
            "classificacao.v1",
            "modelo-de-teste",
            [.. parts.Select(p => new ClassifiedPart(p.Type, new PageRange(p.First, p.Last)))],
            [],
            ModelUsage.None));
}
