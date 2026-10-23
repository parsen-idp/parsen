using System.Text.Json.Nodes;
using Parsen.Schemas;

namespace Parsen.Schemas.Tests;

public class ExtratoBancarioSchemaTests
{
    private static readonly ExtractionDefinition Extrato = ExtractionSchemas.ExtratoBancario;
    private static readonly JsonNode Schema = JsonNode.Parse(Extrato.Schema)!;

    [Fact]
    public void Loads_current_version_reads_every_page_and_allows_long_answers()
    {
        Assert.Equal("extrato_bancario.v1", Extrato.Id);
        Assert.Equal(Extrato.Id, (string?)Schema["title"]);
        Assert.Contains("`registrar_extrato_bancario`", Extrato.SystemPrompt, StringComparison.Ordinal);
        Assert.Null(Extrato.MaxPages);
        Assert.Equal(16_000, Extrato.MaxTokens);
        Assert.Equal(Extrato.Id, ExtractionSchemas.ForDocumentType("extrato_bancario")?.Id);
    }

    [Fact]
    public void Requires_every_property()
    {
        var properties = Schema["properties"]!.AsObject().Select(p => p.Key).Order(StringComparer.Ordinal);
        var required = Schema["required"]!.AsArray().Select(r => (string)r!).Order(StringComparer.Ordinal);

        Assert.Equal(properties, required);
    }

    // Só os créditos (spec, seção 14): sem tipo de lançamento nem categoria, que são regras da Fase 3
    [Fact]
    public void Credit_has_date_description_and_positive_value_with_evidence()
    {
        var credit = Schema["$defs"]!["credito"]!;
        var required = credit["required"]!.AsArray().Select(r => (string)r!).Order(StringComparer.Ordinal);

        Assert.Equal(["data", "descricao", "pagina", "trecho_fonte", "valor"], required);
        Assert.Equal(0, (int)credit["properties"]!["valor"]!["exclusiveMinimum"]!);
    }

    // Saldo pode ser negativo (cheque especial é um fato relevante); valor de crédito não
    [Fact]
    public void Balances_accept_negative_values()
    {
        Assert.Equal("#/$defs/campo_saldo", (string?)Schema["properties"]!["saldo_final"]!["$ref"]);
        Assert.Null(Schema["$defs"]!["campo_saldo"]!["properties"]!["valor"]!["minimum"]);
    }
}
