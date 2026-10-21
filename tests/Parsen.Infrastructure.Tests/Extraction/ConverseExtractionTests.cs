using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Parsen.Application.Pipeline;
using Parsen.Infrastructure.Extraction;
using Parsen.Schemas;

namespace Parsen.Infrastructure.Tests.Extraction;

public sealed class ConverseExtractionTests
{
    private const string ModelId = "global.anthropic.claude-haiku-4-5-20251001-v1:0";

    private static readonly ExtractionDefinition Holerite = ExtractionSchemas.Holerite;
    private static readonly BedrockExtractionSettings Settings = new(ModelId, MaxTokens: 2048);

    private static readonly OcrDocument Ocr = new(
    [
        new OcrPage(1, [Line("EMPRESA FICTICIA LTDA"), Line("Líquido a receber"), Line("4.870,35")]),
        new OcrPage(2, [Line("Página 2")]),
    ]);

    // Saída sintética da ferramenta, no formato do holerite.v2 (nenhum dado real)
    private const string ToolInput = """
        {
          "nome_funcionario": { "valor": "FULANO DE TAL", "pagina": 1, "trecho_fonte": "FULANO DE TAL" },
          "cpf": null,
          "cnpj_empregador": { "valor": "12ABC34501DE35", "pagina": 1, "trecho_fonte": "12.ABC.345/01DE-35" },
          "salario_liquido": { "valor": 4870.35, "pagina": 1, "trecho_fonte": "4.870,35" },
          "itens": [
            { "codigo": "001", "descricao": "SALARIO", "referencia": "30,00", "tipo": "vencimento", "valor": 5200, "pagina": 1, "trecho_fonte": "5.200,00" },
            { "codigo": null, "descricao": "INSS", "referencia": null, "tipo": "desconto", "valor": 329.65, "pagina": 1, "trecho_fonte": "329,65" }
          ],
          "alertas": ["CPF não aparece no documento"]
        }
        """;

    [Fact]
    public void Request_CachesPromptAndToolAndForcesTheTool()
    {
        var request = ConverseExtraction.BuildRequest(Holerite, Settings, Ocr);

        Assert.Equal(ModelId, request.ModelId);
        Assert.Equal(2048, request.InferenceConfig.MaxTokens);
        Assert.Equal(Holerite.SystemPrompt, request.System[0].Text);
        Assert.Equal(CachePointType.Default, request.System[1].CachePoint.Type);

        var tool = Assert.Single(request.ToolConfig.Tools).ToolSpec;
        Assert.Equal("registrar_holerite", tool.Name);
        Assert.Equal("registrar_holerite", request.ToolConfig.ToolChoice.Tool.Name);
        Assert.True(tool.InputSchema.Json.AsDictionary()["properties"].IsDictionary());
    }

    [Fact]
    public void Request_SendsTheOcrAfterTheCachePoint()
    {
        var request = ConverseExtraction.BuildRequest(Holerite, Settings, Ocr);

        var message = Assert.Single(request.Messages);
        Assert.Equal(ConversationRole.User, message.Role);
        Assert.Equal(
            "=== PÁGINA 1 ===\nEMPRESA FICTICIA LTDA\nLíquido a receber\n4.870,35\n=== PÁGINA 2 ===\nPágina 2\n",
            Assert.Single(message.Content).Text);
    }

    [Fact]
    public void Response_BecomesFieldsListsAndAlerts()
    {
        var extraction = ConverseExtraction.ReadResponse(Holerite, ModelId, Response(StopReason.Tool_use, ToolInput));

        Assert.Equal("holerite.v2", extraction.SchemaId);
        Assert.Equal(ModelId, extraction.ModelId);
        Assert.Null(extraction.Fields["cpf"]);
        Assert.Equal(new ModelField(FieldValue.Text("FULANO DE TAL"), 1, "FULANO DE TAL"), extraction.Fields["nome_funcionario"]);
        Assert.Equal(["CPF não aparece no documento"], extraction.Alerts);
        Assert.False(extraction.Fields.ContainsKey("alertas"));
    }

    [Fact]
    public void Response_KeepsTheExactNumberTextForDecimal()
    {
        var extraction = ConverseExtraction.ReadResponse(Holerite, ModelId, Response(StopReason.Tool_use, ToolInput));

        var liquido = extraction.Fields["salario_liquido"]!;
        Assert.Equal(FieldValue.Number("4870.35"), liquido.Value);
        Assert.Equal(4870.35m, liquido.Value.ToDecimal());
    }

    [Fact]
    public void Response_ListItemsKeepAttributesAndSource()
    {
        var extraction = ConverseExtraction.ReadResponse(Holerite, ModelId, Response(StopReason.Tool_use, ToolInput));

        var itens = extraction.Lists["itens"];
        Assert.Equal(2, itens.Count);
        Assert.Equal(FieldValue.Number("5200"), itens[0].Attributes["valor"]);
        Assert.Null(itens[1].Attributes["codigo"]);
        Assert.Equal(FieldValue.Text("desconto"), itens[1].Attributes["tipo"]);
        Assert.Equal("329,65", itens[1].SourceText);
        Assert.False(itens[1].Attributes.ContainsKey("trecho_fonte"));
    }

    [Fact]
    public void CutByMaxTokens_IsRejectedWithoutRetry()
    {
        var error = Assert.Throws<DocumentRejectedException>(
            () => ConverseExtraction.ReadResponse(Holerite, ModelId, Response(StopReason.Max_tokens, ToolInput)));
        Assert.Equal("EXTRACAO_INCOMPLETA", error.Code);
    }

    [Theory]
    [InlineData("refusal")]
    [InlineData("content_filtered")]
    [InlineData("guardrail_intervened")]
    public void Refused_IsRejectedWithoutRetry(string stopReason)
    {
        var error = Assert.Throws<DocumentRejectedException>(
            () => ConverseExtraction.ReadResponse(Holerite, ModelId, Response(new StopReason(stopReason), ToolInput)));
        Assert.Equal("EXTRACAO_RECUSADA", error.Code);
    }

    [Fact]
    public void WithoutTheToolCall_IsATechnicalError()
    {
        var response = Response(StopReason.End_turn, ToolInput);
        response.Output.Message.Content = [new ContentBlock { Text = "texto solto" }];

        Assert.Throws<InvalidDataException>(() => ConverseExtraction.ReadResponse(Holerite, ModelId, response));
    }

    [Fact]
    public void FieldWithoutSource_IsATechnicalError()
    {
        const string input = """{ "salario_liquido": { "valor": 10, "pagina": 1 } }""";

        Assert.Throws<InvalidDataException>(
            () => ConverseExtraction.ReadResponse(Holerite, ModelId, Response(StopReason.Tool_use, input)));
    }

    private static ConverseResponse Response(StopReason stopReason, string toolInput) => new()
    {
        StopReason = stopReason,
        Output = new ConverseOutput
        {
            Message = new Message
            {
                Role = ConversationRole.Assistant,
                Content =
                [
                    new ContentBlock
                    {
                        ToolUse = new ToolUseBlock { ToolUseId = "tooluse_1", Name = "registrar_holerite", Input = ConverseExtraction.ToDocument(toolInput) },
                    },
                ],
            },
        },
    };

    private static OcrLine Line(string text) => new(text, 99, [0, 0, 1, 0.02], []);
}
