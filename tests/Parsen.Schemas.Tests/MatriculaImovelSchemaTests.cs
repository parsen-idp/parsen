using System.Text.Json.Nodes;
using Parsen.Schemas;

namespace Parsen.Schemas.Tests;

public class MatriculaImovelSchemaTests
{
    private static readonly ExtractionDefinition Matricula = ExtractionSchemas.MatriculaImovel;
    private static readonly JsonNode Schema = JsonNode.Parse(Matricula.Schema)!;

    [Fact]
    public void Loads_current_version_reads_every_page_and_allows_long_answers()
    {
        Assert.Equal("matricula_imovel.v1", Matricula.Id);
        Assert.Equal(Matricula.Id, (string?)Schema["title"]);
        Assert.Contains("`registrar_matricula_imovel`", Matricula.SystemPrompt, StringComparison.Ordinal);
        Assert.Null(Matricula.MaxPages);
        Assert.Equal(8_000, Matricula.MaxTokens);
        Assert.Equal(Matricula.Id, ExtractionSchemas.ForDocumentType("matricula_imovel")?.Id);
    }

    [Fact]
    public void Requires_every_property()
    {
        var properties = Schema["properties"]!.AsObject().Select(p => p.Key).Order(StringComparer.Ordinal);
        var required = Schema["required"]!.AsArray().Select(r => (string)r!).Order(StringComparer.Ordinal);

        Assert.Equal(properties, required);
    }

    // As conclusões (proprietário atual, ônus ativos) são regras da Fase 3: o schema só tem o histórico
    [Fact]
    public void Has_history_but_no_conclusions()
    {
        var properties = Schema["properties"]!.AsObject().Select(p => p.Key).ToList();

        Assert.Contains("atos", properties);
        Assert.Contains("partes", properties);
        Assert.DoesNotContain(properties, p => p.Contains("atual", StringComparison.Ordinal) || p.Contains("ativo", StringComparison.Ordinal));
    }

    // Minimização: das partes, só nome e CPF/CNPJ (sem profissão, estado civil, endereço)
    [Fact]
    public void Party_keeps_only_name_and_tax_id()
    {
        var party = Schema["$defs"]!["parte"]!["properties"]!.AsObject().Select(p => p.Key).Order(StringComparer.Ordinal);

        Assert.Equal(["ato", "cpf_cnpj", "nome", "pagina", "papel", "trecho_fonte"], party);
    }

    [Fact]
    public void Act_number_pattern_accepts_registers_and_annotations()
    {
        var pattern = (string)Schema["$defs"]!["ato"]!["properties"]!["numero"]!["pattern"]!;

        Assert.Matches(pattern, "R.1");
        Assert.Matches(pattern, "Av.12");
        Assert.DoesNotMatch(pattern, "R.1/45.678");
        Assert.DoesNotMatch(pattern, "AV-3");
    }
}
