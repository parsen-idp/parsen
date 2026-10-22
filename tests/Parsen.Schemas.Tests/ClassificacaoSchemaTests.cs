using System.Text.Json.Nodes;
using Parsen.Domain.Dossiers;
using Parsen.Schemas;

namespace Parsen.Schemas.Tests;

public class ClassificacaoSchemaTests
{
    private static readonly ExtractionDefinition Classificacao = ExtractionSchemas.Classificacao;

    [Fact]
    public void Classificacao_loads_current_version_and_names_the_tool()
    {
        Assert.Equal("classificacao.v1", Classificacao.Id);
        Assert.Equal("registrar_classificacao", Classificacao.ToolName);
        Assert.Equal(Classificacao.Id, (string?)JsonNode.Parse(Classificacao.Schema)!["title"]);
        Assert.Contains($"`{Classificacao.ToolName}`", Classificacao.SystemPrompt, StringComparison.Ordinal);
    }

    // O schema e o domínio precisam ter os mesmos tipos: um tipo novo de um lado só quebraria a leitura da resposta
    [Fact]
    public void Type_enum_matches_the_domain_codes()
    {
        var types = JsonNode.Parse(Classificacao.Schema)!["properties"]!["documentos"]!["items"]!["properties"]!["tipo"]!["enum"]!
            .AsArray().Select(t => (string)t!).Order(StringComparer.Ordinal);

        Assert.Equal(DocumentTypeCodes.All.Order(StringComparer.Ordinal), types);
    }

    [Fact]
    public void Prompt_describes_every_type()
    {
        Assert.All(DocumentTypeCodes.All, code => Assert.Contains($"`{code}`", Classificacao.SystemPrompt, StringComparison.Ordinal));
    }
}
