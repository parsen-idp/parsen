using System.Text.Json.Nodes;
using Parsen.Schemas;

namespace Parsen.Schemas.Tests;

public class ComprovanteResidenciaSchemaTests
{
    private static readonly ExtractionDefinition Comprovante = ExtractionSchemas.ComprovanteResidencia;
    private static readonly JsonNode Schema = JsonNode.Parse(Comprovante.Schema)!;

    [Fact]
    public void Loads_current_version_names_the_tool_and_limits_pages()
    {
        Assert.Equal("comprovante_residencia.v1", Comprovante.Id);
        Assert.Equal(Comprovante.Id, (string?)Schema["title"]);
        Assert.Contains("`registrar_comprovante_residencia`", Comprovante.SystemPrompt, StringComparison.Ordinal);
        Assert.Equal(2, Comprovante.MaxPages);
        Assert.Equal(Comprovante.Id, ExtractionSchemas.ForDocumentType("comprovante_residencia")?.Id);
    }

    [Fact]
    public void Requires_every_property()
    {
        var properties = Schema["properties"]!.AsObject().Select(p => p.Key).Order(StringComparer.Ordinal);
        var required = Schema["required"]!.AsArray().Select(r => (string)r!).Order(StringComparer.Ordinal);

        Assert.Equal(properties, required);
    }

    [Fact]
    public void References_resolve()
    {
        var defs = Schema["$defs"]!.AsObject();
        var refs = Schema["properties"]!.AsObject()
            .Select(p => (string?)p.Value!["$ref"])
            .OfType<string>()
            .ToList();

        Assert.NotEmpty(refs);
        Assert.All(refs, r => Assert.True(defs.ContainsKey(r.Replace("#/$defs/", "", StringComparison.Ordinal)), $"$ref sem definição: {r}"));
    }

    // MVP: só contas de consumo (spec, seção 14); IPTU, condomínio e cartão são classificados como "outro"
    [Fact]
    public void Subtypes_are_only_utility_bills()
    {
        var subtypes = Schema["$defs"]!["campo_subtipo"]!["properties"]!["valor"]!["enum"]!.AsArray().Select(s => (string)s!);

        Assert.Equal(["energia", "agua", "gas", "telecom"], subtypes);
    }

    [Fact]
    public void Patterns_accept_normalized_values()
    {
        var defs = Schema["$defs"]!;
        string Pattern(string name) => (string)defs[name]!["properties"]!["valor"]!["pattern"]!;

        Assert.Matches(Pattern("campo_cep"), "01234567");
        Assert.DoesNotMatch(Pattern("campo_cep"), "01234-567");
        Assert.Matches(Pattern("campo_uf"), "SP");
        Assert.DoesNotMatch(Pattern("campo_uf"), "São Paulo");
        Assert.Matches(Pattern("campo_cnpj"), "12ABC34501DE35");
    }
}
