using Parsen.Application.Pipeline;
using Parsen.Eval;

namespace Parsen.Eval.Tests;

public sealed class ComparerTests
{
    private static readonly Gabarito Expected = Gabarito.Parse("""
        {
          "document_type": "holerite",
          "fields": { "nome_funcionario": "Ana  Lima", "cpf": null, "salario_liquido": 4870.35, "cargo": "VENDEDOR", "total_descontos": 100 },
          "lists": { "itens": [
            { "tipo": "vencimento", "valor": 5000.00, "descricao": "SALARIO" },
            { "tipo": "desconto", "valor": 129.65, "descricao": "INSS" },
            { "tipo": "desconto", "valor": 129.65, "descricao": "INSS" }
          ] }
        }
        """);

    [Fact]
    public void Text_IgnoresCaseAndExtraSpaces()
    {
        var score = Compare(Field("nome_funcionario", FieldValue.Text("ANA LIMA")));

        Assert.Equal(FieldOutcome.Correct, Outcome(score, "nome_funcionario"));
    }

    [Fact]
    public void Number_ComparesAsExactDecimal()
    {
        Assert.Equal(FieldOutcome.Correct, Outcome(Compare(Field("salario_liquido", FieldValue.Number("4870.350"))), "salario_liquido"));
        Assert.Equal(FieldOutcome.Wrong, Outcome(Compare(Field("salario_liquido", FieldValue.Number("4870.36"))), "salario_liquido"));
    }

    [Fact]
    public void ValueWhereTheDocumentHasNone_IsInvented()
    {
        Assert.Equal(FieldOutcome.Invented, Outcome(Compare(Field("cpf", FieldValue.Text("12345678909"))), "cpf"));
    }

    [Fact]
    public void BothAbsent_IsCorrect()
    {
        Assert.Equal(FieldOutcome.Correct, Outcome(Compare(new ExtractedField("cpf", null, null, null)), "cpf"));
    }

    [Fact]
    public void NullWhereTheDocumentHasAValue_IsMissed()
    {
        Assert.Equal(FieldOutcome.Missed, Outcome(Compare(new ExtractedField("cargo", null, null, null)), "cargo"));
    }

    [Fact]
    public void FieldAbsentFromTheExtraction_CountsAsNull()
    {
        Assert.Equal(FieldOutcome.Missed, Outcome(Compare(), "total_descontos"));
    }

    [Fact]
    public void Located_ComesFromTheConfidence()
    {
        var located = Compare(Field("salario_liquido", FieldValue.Number("4870.35"), confidence: 0.97));
        var notLocated = Compare(Field("salario_liquido", FieldValue.Number("4870.35"), confidence: 0));

        Assert.True(located.Fields.Single(f => f.Field == "salario_liquido").Located);
        Assert.False(notLocated.Fields.Single(f => f.Field == "salario_liquido").Located);
    }

    [Fact]
    public void ListItems_MatchByTypeAndValue_EachAtMostOnce()
    {
        var score = Comparer.Compare(Expected, Result(
            [],
            Item("vencimento", "5000"),
            Item("desconto", "129.65"),
            Item("desconto", "99.99")));

        var itens = Assert.Single(score.Lists);
        Assert.Equal(3, itens.Expected);
        Assert.Equal(2, itens.Matched);
        Assert.Equal(1, itens.Extra);
    }

    private static CaseScore Compare(params ExtractedField[] fields) => Comparer.Compare(Expected, Result(fields));

    private static FieldOutcome Outcome(CaseScore score, string field) => score.Fields.Single(f => f.Field == field).Outcome;

    private static ExtractedField Field(string name, FieldValue value, double confidence = 0.99) =>
        new(name, value, new Evidence(1, value.Raw, [0, 0, 0.1, 0.01]), confidence);

    private static ExtractedItem Item(string tipo, string valor) => new(
        new Dictionary<string, FieldValue?> { ["tipo"] = FieldValue.Text(tipo), ["valor"] = FieldValue.Number(valor) },
        new Evidence(1, valor, null),
        0.9);

    private static ExtractionResult Result(ExtractedField[] fields, params ExtractedItem[] items) => new(
        "holerite",
        "holerite.v2",
        "modelo-de-teste",
        fields,
        new Dictionary<string, IReadOnlyList<ExtractedItem>> { ["itens"] = items },
        [],
        ModelUsage.None,
        DateTimeOffset.UnixEpoch);
}
