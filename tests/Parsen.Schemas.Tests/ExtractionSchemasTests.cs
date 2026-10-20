using System.Text.Json;
using System.Text.Json.Nodes;
using Parsen.Schemas;

namespace Parsen.Schemas.Tests;

public class ExtractionSchemasTests
{
    private static readonly ExtractionDefinition Holerite = ExtractionSchemas.Holerite;

    [Fact]
    public void Holerite_loads_schema_and_prompt_of_current_version()
    {
        Assert.Equal("holerite.v1", Holerite.Id);
        Assert.Equal("registrar_holerite", Holerite.ToolName);
        Assert.NotEmpty(Holerite.Schema);
        Assert.NotEmpty(Holerite.SystemPrompt);
    }

    [Fact]
    public void Holerite_schema_title_matches_version()
    {
        var schema = JsonNode.Parse(Holerite.Schema)!;
        Assert.Equal(Holerite.Id, (string?)schema["title"]);
    }

    [Fact]
    public void Holerite_prompt_names_the_tool()
    {
        Assert.Contains($"`{Holerite.ToolName}`", Holerite.SystemPrompt, StringComparison.Ordinal);
    }

    // O modelo precisa devolver todos os campos (null quando ausente): campo opcional some e vira dúvida na verificação
    [Fact]
    public void Holerite_schema_requires_every_property()
    {
        var schema = JsonNode.Parse(Holerite.Schema)!;
        var properties = schema["properties"]!.AsObject().Select(p => p.Key).Order();
        var required = schema["required"]!.AsArray().Select(r => (string)r!).Order();

        Assert.Equal(properties, required);
    }

    [Fact]
    public void Holerite_schema_references_resolve()
    {
        var schema = JsonNode.Parse(Holerite.Schema)!;
        var defs = schema["$defs"]!.AsObject();

        var refs = Descendants(schema)
            .OfType<JsonObject>()
            .Select(o => (string?)o["$ref"])
            .OfType<string>()
            .ToList();

        Assert.NotEmpty(refs);
        Assert.All(refs, r => Assert.True(defs.ContainsKey(r.Replace("#/$defs/", "", StringComparison.Ordinal)), $"$ref sem definição: {r}"));
    }

    [Fact]
    public void Holerite_schema_identification_patterns_accept_valid_formats()
    {
        var defs = JsonNode.Parse(Holerite.Schema)!["$defs"]!;

        Assert.Matches(Pattern(defs, "campo_cpf"), "12345678909");
        Assert.DoesNotMatch(Pattern(defs, "campo_cpf"), "123.456.789-09");
        Assert.Matches(Pattern(defs, "campo_cnpj"), "12345678000195");
        Assert.Matches(Pattern(defs, "campo_cnpj"), "12ABC34501DE35");
        Assert.DoesNotMatch(Pattern(defs, "campo_cnpj"), "12ABC34501DEAB");
        Assert.Matches(Pattern(defs, "campo_competencia"), "2026-03");
        Assert.DoesNotMatch(Pattern(defs, "campo_competencia"), "2026-13");
        Assert.Matches(Pattern(defs, "campo_data"), "2024-02-29");
        Assert.DoesNotMatch(Pattern(defs, "campo_data"), "29/02/2024");
    }

    [Fact]
    public void Load_unknown_version_fails_clearly()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ExtractionSchemas.Load("holerite", "v999"));
        Assert.Contains("holerite.v999", error.Message, StringComparison.Ordinal);
    }

    private static string Pattern(JsonNode defs, string name) => (string)defs[name]!["properties"]!["valor"]!["pattern"]!;

    private static IEnumerable<JsonNode> Descendants(JsonNode node)
    {
        yield return node;
        IEnumerable<JsonNode?> children = node switch
        {
            JsonObject o => o.Select(p => p.Value),
            JsonArray a => a,
            _ => [],
        };
        foreach (var child in children.OfType<JsonNode>())
        {
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}
