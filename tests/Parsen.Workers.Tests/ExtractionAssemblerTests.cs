using Parsen.Application.Pipeline;

namespace Parsen.Workers.Tests;

/// <summary>
/// CPF e CNPJ saem do trecho achado no OCR, não da reescrita do modelo: no Parsen.Eval o Haiku trocou,
/// tirou e pôs caracteres em CNPJ alfanuméricos, mas sempre apontou o trecho certo.
/// </summary>
public sealed class ExtractionAssemblerTests
{
    private static readonly OcrDocument Ocr = new(
    [
        new OcrPage(1,
        [
            Line("CNPJ: 21.6X5.92R/0001-24"),
            Line("508.531.078-07"),
            Line("ZETA ALIMENTOS LTDA"),
        ]),
    ]);

    [Fact]
    public void Cnpj_ComesFromTheOcrSnippet_EvenWhenTheModelRewroteIt()
    {
        var result = Assemble(("cnpj_empregador", FieldValue.Text("21K6X5L92R000124"), "21.6X5.92R/0001-24"));

        Assert.Equal(FieldValue.Text("216X592R000124"), Field(result, "cnpj_empregador").Value);
    }

    [Fact]
    public void Cnpj_SnippetWithLabel_KeepsOnlyTheNumber()
    {
        var result = Assemble(("cnpj_empregador", FieldValue.Text("216X592R000124"), "CNPJ: 21.6X5.92R/0001-24"));

        Assert.Equal(FieldValue.Text("216X592R000124"), Field(result, "cnpj_empregador").Value);
    }

    [Fact]
    public void Cpf_ComesFromTheOcrSnippet()
    {
        var result = Assemble(("cpf", FieldValue.Text("50853107870"), "508.531.078-07"));

        Assert.Equal(FieldValue.Text("50853107807"), Field(result, "cpf").Value);
    }

    [Fact]
    public void SnippetNotInTheOcr_KeepsTheModelValue()
    {
        var result = Assemble(("cnpj_empregador", FieldValue.Text("11222333000181"), "11.222.333/0001-81"));

        var cnpj = Field(result, "cnpj_empregador");
        Assert.Equal(FieldValue.Text("11222333000181"), cnpj.Value);
        Assert.Equal(0, cnpj.Confidence);
    }

    [Fact]
    public void SnippetWithoutAnIdentifier_KeepsTheModelValue()
    {
        var result = Assemble(("cnpj_empregador", FieldValue.Text("216X592R000124"), "ZETA ALIMENTOS LTDA"));

        Assert.Equal(FieldValue.Text("216X592R000124"), Field(result, "cnpj_empregador").Value);
    }

    [Fact]
    public void OtherFields_AreNotTouched()
    {
        var result = Assemble(("empregador", FieldValue.Text("Zeta Alimentos"), "ZETA ALIMENTOS LTDA"));

        Assert.Equal(FieldValue.Text("Zeta Alimentos"), Field(result, "empregador").Value);
    }

    private static ExtractionResult Assemble(params (string Name, FieldValue Value, string Source)[] fields) =>
        ExtractionAssembler.Assemble(
            "holerite",
            new ModelExtraction(
                "holerite.v2",
                "modelo-de-teste",
                fields.ToDictionary(f => f.Name, f => (ModelField?)new ModelField(f.Value, 1, f.Source)),
                new Dictionary<string, IReadOnlyList<ModelItem>>(),
                [],
                ModelUsage.None),
            Ocr,
            DateTimeOffset.UnixEpoch);

    private static ExtractedField Field(ExtractionResult result, string name) => result.Fields.Single(f => f.Name == name);

    private static OcrLine Line(string text) =>
        new(text, 99, [0, 0, 0.5, 0.02], [.. text.Split(' ').Select(w => new OcrWord(w, 99, [0, 0, 0.1, 0.02]))]);
}
