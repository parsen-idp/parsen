using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Parsen.Application.Pipeline;
using Parsen.Domain.Dossiers;
using Parsen.Infrastructure.Extraction;
using Parsen.Schemas;

namespace Parsen.Infrastructure.Tests.Extraction;

public sealed class ConverseClassificationTests
{
    private const string ModelId = "global.anthropic.claude-haiku-4-5-20251001-v1:0";

    private static readonly ExtractionDefinition Classificacao = ExtractionSchemas.Classificacao;

    [Fact]
    public void Excerpts_SendOnlyTheFirstLinesOfEachPage()
    {
        var ocr = new OcrDocument(
        [
            new OcrPage(1, [.. Enumerable.Range(1, 30).Select(i => Line($"linha {i}"))]),
            new OcrPage(2, [Line("CONTINUAÇÃO")]),
        ]);

        var text = ConverseClassification.FormatExcerpts(ocr, linesPerPage: 3);

        Assert.Equal("=== PÁGINA 1 de 2 ===\nlinha 1\nlinha 2\nlinha 3\n=== PÁGINA 2 de 2 ===\nCONTINUAÇÃO\n", text);
    }

    [Fact]
    public void Request_UsesTheClassificationSchemaAsTheForcedTool()
    {
        var request = ConverseExtraction.BuildRequest(Classificacao, new BedrockExtractionSettings(ModelId, MaxTokens: 1024), "texto");

        Assert.Equal("registrar_classificacao", Assert.Single(request.ToolConfig.Tools).ToolSpec.Name);
        Assert.Equal("registrar_classificacao", request.ToolConfig.ToolChoice.Tool.Name);
        Assert.Equal(Classificacao.SystemPrompt, request.System[0].Text);
        Assert.Equal(1024, request.InferenceConfig.MaxTokens);
    }

    [Fact]
    public void Response_BecomesPartsAlertsAndUsage()
    {
        var output = ConverseClassification.ReadResponse(Classificacao, ModelId, Response("""
            {
              "documentos": [
                { "tipo": "holerite", "pagina_inicial": 1, "pagina_final": 1 },
                { "tipo": "extrato_bancario", "pagina_inicial": 2, "pagina_final": 4 }
              ],
              "alertas": ["página 5 em branco"]
            }
            """));

        Assert.Equal(Classificacao.Id, output.SchemaId);
        Assert.Equal(
            [new ClassifiedPart(DocumentType.Holerite, new PageRange(1, 1)), new ClassifiedPart(DocumentType.ExtratoBancario, new PageRange(2, 4))],
            output.Parts);
        Assert.Equal(["página 5 em branco"], output.Alerts);
        Assert.Equal(new ModelUsage(900, 80, 0, 0), output.Usage);
    }

    [Fact]
    public void UnknownType_IsATechnicalError()
    {
        Assert.Throws<InvalidDataException>(() => ConverseClassification.ReadResponse(Classificacao, ModelId,
            Response("""{ "documentos": [ { "tipo": "rg", "pagina_inicial": 1, "pagina_final": 1 } ], "alertas": [] }""")));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(3, 2)]
    public void InvalidPageRange_IsATechnicalError(int first, int last)
    {
        Assert.Throws<InvalidDataException>(() => ConverseClassification.ReadResponse(Classificacao, ModelId,
            Response($$"""{ "documentos": [ { "tipo": "holerite", "pagina_inicial": {{first}}, "pagina_final": {{last}} } ], "alertas": [] }""")));
    }

    [Fact]
    public void CutByMaxTokens_IsRejectedWithoutRetry()
    {
        var response = Response("""{ "documentos": [], "alertas": [] }""");
        response.StopReason = StopReason.Max_tokens;

        Assert.Throws<DocumentRejectedException>(() => ConverseClassification.ReadResponse(Classificacao, ModelId, response));
    }

    private static ConverseResponse Response(string toolInput) => new()
    {
        StopReason = StopReason.Tool_use,
        Usage = new TokenUsage { InputTokens = 900, OutputTokens = 80, TotalTokens = 980 },
        Output = new ConverseOutput
        {
            Message = new Message
            {
                Role = ConversationRole.Assistant,
                Content =
                [
                    new ContentBlock
                    {
                        ToolUse = new ToolUseBlock { ToolUseId = "tooluse_1", Name = "registrar_classificacao", Input = ConverseExtraction.ToDocument(toolInput) },
                    },
                ],
            },
        },
    };

    private static OcrLine Line(string text) => new(text, 99, [0, 0, 1, 0.02], []);
}
