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
            Line("CEP 01234-567 EXEMPLÓPOLIS - SP"),
            Line("CPF: ***.531.078-**"),
            Line("CNPJ: 61.***.***/****-93"),
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
    public void Cep_ComesFromTheOcrSnippet()
    {
        var result = Assemble(("cep", FieldValue.Text("01234576"), "CEP 01234-567 EXEMPLÓPOLIS - SP"));

        Assert.Equal(FieldValue.Text("01234567"), Field(result, "cep").Value);
    }

    // Visto na conta da Enel: o OCR tinha "61.***.***/****-93" e o modelo devolveu o CNPJ completo, da memória.
    // Identificador que não está inteiro no trecho achado não existe no documento: vira null, com alerta
    [Fact]
    public void MaskedCnpj_BecomesNullWithAlert()
    {
        var result = Assemble(("cnpj_emissor", FieldValue.Text("61695227000193"), "61.***.***/****-93"));

        var cnpj = Field(result, "cnpj_emissor");
        Assert.Null(cnpj.Value);
        Assert.NotNull(cnpj.Evidence);
        Assert.Contains(result.Alerts, a => a.Contains("cnpj_emissor", StringComparison.Ordinal));
    }

    [Fact]
    public void MaskedCpf_BecomesNull()
    {
        var result = Assemble(("cpf_titular", FieldValue.Text("53107800000"), "CPF: ***.531.078-**"));

        Assert.Null(Field(result, "cpf_titular").Value);
    }

    // Visto no Eval com a conta da Enel: o modelo inventou também o trecho (o CNPJ completo, que não está no OCR).
    // Identificador sem evidência não é entregue
    [Fact]
    public void IdentifierWithSnippetNotInTheOcr_BecomesNullWithAlert()
    {
        var result = Assemble(("cnpj_empregador", FieldValue.Text("11222333000181"), "11.222.333/0001-81"));

        var cnpj = Field(result, "cnpj_empregador");
        Assert.Null(cnpj.Value);
        Assert.Equal(0, cnpj.Confidence);
        Assert.Contains(result.Alerts, a => a.Contains("cnpj_empregador", StringComparison.Ordinal));
    }

    // Campo comum sem trecho achado continua com o valor do modelo e confiança 0 (vai para revisão)
    [Fact]
    public void OtherFieldWithSnippetNotInTheOcr_KeepsTheModelValueWithZeroConfidence()
    {
        var result = Assemble(("empregador", FieldValue.Text("EMPRESA INEXISTENTE"), "EMPRESA INEXISTENTE"));

        var field = Field(result, "empregador");
        Assert.Equal(FieldValue.Text("EMPRESA INEXISTENTE"), field.Value);
        Assert.Equal(0, field.Confidence);
    }

    // O trecho apontado não mostra o número: o valor não tem evidência no documento
    [Fact]
    public void SnippetWithoutAnIdentifier_BecomesNull()
    {
        var result = Assemble(("cnpj_empregador", FieldValue.Text("216X592R000124"), "ZETA ALIMENTOS LTDA"));

        Assert.Null(Field(result, "cnpj_empregador").Value);
    }

    [Fact]
    public void FullIdentifier_DoesNotRaiseAlerts()
    {
        var result = Assemble(("cpf", FieldValue.Text("50853107807"), "508.531.078-07"));

        Assert.Empty(result.Alerts);
    }

    [Fact]
    public void OtherFields_AreNotTouched()
    {
        var result = Assemble(("empregador", FieldValue.Text("Zeta Alimentos"), "ZETA ALIMENTOS LTDA"));

        Assert.Equal(FieldValue.Text("Zeta Alimentos"), Field(result, "empregador").Value);
    }

    // Matrícula real: o número do ato "R.10/…" foi lido com 99%, mas o valor "5.000,00" (era 115.000,00) com 80%.
    // A confiança do item é a menor entre o trecho principal e os trechos extras (trecho_valor)
    [Fact]
    public void ListItem_ConfidenceIsTheLowestAmongItsSnippets()
    {
        var ocr = new OcrDocument(
        [
            new OcrPage(3,
            [
                new OcrLine("R.10/130.275 - COMPRA E VENDA", 99, [0, 0, 1, 0.02], [new OcrWord("R.10/130.275", 99.0, [0, 0, 0.2, 0.02])]),
                new OcrLine("pelo preço de R$ 5.000,00.", 93, [0, 0.1, 1, 0.02], [new OcrWord("5.000,00.", 80.19, [0.5, 0.1, 0.1, 0.02])]),
            ]),
        ]);
        var item = new ModelItem(
            new Dictionary<string, FieldValue?> { ["numero"] = FieldValue.Text("R.10"), ["valor"] = FieldValue.Number("5000"), ["trecho_valor"] = FieldValue.Text("5.000,00.") },
            3,
            "R.10/130.275");

        var result = ExtractionAssembler.Assemble(
            "matricula_imovel",
            new ModelExtraction("matricula_imovel.v2", "modelo-de-teste", new Dictionary<string, ModelField?>(), new Dictionary<string, IReadOnlyList<ModelItem>> { ["atos"] = [item] }, [], ModelUsage.None),
            ocr,
            DateTimeOffset.UnixEpoch);

        var ato = Assert.Single(result.Lists["atos"]);
        Assert.Equal(0.8, ato.Confidence);
        Assert.Equal("R.10/130.275", ato.Evidence.Text);
        Assert.False(ato.Attributes.ContainsKey("trecho_valor"));
    }

    // Trecho extra que não está no OCR derruba a confiança para 0: o valor não tem evidência
    [Fact]
    public void ListItem_ExtraSnippetNotInTheOcr_HasZeroConfidence()
    {
        var item = new ModelItem(
            new Dictionary<string, FieldValue?> { ["valor"] = FieldValue.Number("115000"), ["trecho_valor"] = FieldValue.Text("115.000,00") },
            1,
            "508.531.078-07");

        var result = ExtractionAssembler.Assemble(
            "matricula_imovel",
            new ModelExtraction("matricula_imovel.v2", "modelo-de-teste", new Dictionary<string, ModelField?>(), new Dictionary<string, IReadOnlyList<ModelItem>> { ["atos"] = [item] }, [], ModelUsage.None),
            Ocr,
            DateTimeOffset.UnixEpoch);

        Assert.Equal(0, Assert.Single(result.Lists["atos"]).Confidence);
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
