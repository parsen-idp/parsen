using Parsen.Application.Pipeline;

namespace Parsen.Workers.Tests;

public sealed class EvidenceLocatorTests
{
    // Página 1: rótulo e valor em linhas diferentes (layout em colunas); página 2 repete um valor da página 1
    private static readonly OcrDocument Ocr = new(
    [
        new OcrPage(1,
        [
            Line("Salário Base", 99.1, [0.10, 0.20, 0.15, 0.02], Word("Salário", 99.0, [0.10, 0.20, 0.07, 0.02]), Word("Base", 99.2, [0.18, 0.20, 0.07, 0.02])),
            Line("SALARIO 30,00 1.234,56", 97.0, [0.10, 0.30, 0.60, 0.02],
                Word("SALARIO", 99.5, [0.10, 0.30, 0.10, 0.02]),
                Word("30,00", 88.4, [0.40, 0.30, 0.05, 0.02]),
                Word("1.234,56", 95.37, [0.60, 0.30, 0.10, 0.02])),
            Line("Total R$1.000,00", 91.0, [0.50, 0.80, 0.30, 0.03],
                Word("Total", 99.0, [0.50, 0.80, 0.05, 0.03]),
                Word("R$1.000,00", 72.5, [0.60, 0.80, 0.20, 0.03])),
        ]),
        new OcrPage(2,
        [
            Line("Líquido 4.870,35", 98.0, [0.20, 0.50, 0.30, 0.02],
                Word("Líquido", 98.0, [0.20, 0.50, 0.10, 0.02]),
                Word("4.870,35", 97.0, [0.35, 0.50, 0.15, 0.02])),
            Line("1.234,56", 60.0, [0.70, 0.90, 0.10, 0.02], Word("1.234,56", 60.0, [0.70, 0.90, 0.10, 0.02])),
        ]),
    ]);

    [Fact]
    public void ExactWords_UseOnlyTheirBoxAndTheLowestWordConfidence()
    {
        var located = EvidenceLocator.Locate(Ocr, page: 1, "30,00 1.234,56");

        Assert.Equal(1, located.Evidence.Page);
        Assert.Equal("30,00 1.234,56", located.Evidence.Text);
        Assert.Equal([0.40, 0.30, 0.30, 0.02], located.Evidence.Bbox!);
        Assert.Equal(0.88, located.Confidence);
    }

    [Fact]
    public void SingleWord_IsScaledToZeroToOne()
    {
        var located = EvidenceLocator.Locate(Ocr, page: 1, "1.234,56");

        Assert.Equal([0.60, 0.30, 0.10, 0.02], located.Evidence.Bbox!);
        Assert.Equal(0.95, located.Confidence);
    }

    [Fact]
    public void SnippetInsideALongerWord_FallsBackToTheWholeLine()
    {
        var located = EvidenceLocator.Locate(Ocr, page: 1, "1.000,00");

        Assert.Equal([0.50, 0.80, 0.30, 0.03], located.Evidence.Bbox!);
        Assert.Equal(0.91, located.Confidence);
    }

    [Fact]
    public void ExtraWhitespaceInTheSnippet_IsIgnored()
    {
        var located = EvidenceLocator.Locate(Ocr, page: 1, "  Salário   Base ");

        Assert.Equal("Salário Base", located.Evidence.Text);
        Assert.Equal(0.99, located.Confidence);
    }

    [Fact]
    public void TextOnSeveralPages_PrefersThePageGivenByTheModel()
    {
        var located = EvidenceLocator.Locate(Ocr, page: 2, "1.234,56");

        Assert.Equal(2, located.Evidence.Page);
        Assert.Equal(0.6, located.Confidence);
    }

    [Fact]
    public void WrongPageFromTheModel_IsCorrectedToWhereTheTextIs()
    {
        var located = EvidenceLocator.Locate(Ocr, page: 1, "4.870,35");

        Assert.Equal(2, located.Evidence.Page);
        Assert.Equal(0.97, located.Confidence);
    }

    [Fact]
    public void SnippetNotInTheOcr_HasZeroConfidenceAndNoBox()
    {
        var located = EvidenceLocator.Locate(Ocr, page: 1, "Salário Base 1.234,56");

        Assert.Equal(1, located.Evidence.Page);
        Assert.Equal("Salário Base 1.234,56", located.Evidence.Text);
        Assert.Null(located.Evidence.Bbox);
        Assert.Equal(0, located.Confidence);
    }

    private static OcrLine Line(string text, double confidence, double[] bbox, params OcrWord[] words) => new(text, confidence, bbox, words);

    private static OcrWord Word(string text, double confidence, double[] bbox) => new(text, confidence, bbox);
}
