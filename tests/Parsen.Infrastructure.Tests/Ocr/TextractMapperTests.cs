using System.Text.Json;
using Amazon.Textract;
using Amazon.Textract.Model;
using Parsen.Application.Pipeline;
using Parsen.Infrastructure.Ocr;

namespace Parsen.Infrastructure.Tests.Ocr;

/// <summary>Blocos montados no teste, no formato da resposta do Textract: nada é enviado à AWS.</summary>
public sealed class TextractMapperTests
{
    [Fact]
    public void ToPage_KeepsLinesInOrderWithTheirWordsConfidenceAndBox()
    {
        Block[] blocks =
        [
            new() { Id = "p1", BlockType = BlockType.PAGE },
            Line("l1", "Valor Líquido 4.870,35", 99.61f, 0.62f, 0.81f, ["w1", "w2", "w3"]),
            Word("w1", "Valor", 99.9f, 0.62f),
            Word("w2", "Líquido", 99.8f, 0.66f),
            Word("w3", "4.870,35", 98.7f, 0.74f),
            Line("l2", "Salário Base", 97.12345f, 0.05f, 0.90f, ["w4", "w5"]),
            Word("w4", "Salário", 97.1f, 0.05f),
            Word("w5", "Base", 97.2f, 0.10f),
        ];

        var page = TextractMapper.ToPage(blocks, pageNumber: 2);

        Assert.Equal(2, page.Page);
        Assert.Equal(["Valor Líquido 4.870,35", "Salário Base"], page.Lines.Select(l => l.Text));

        var first = page.Lines[0];
        Assert.Equal(99.61, first.Confidence);
        Assert.Equal([0.62, 0.81, 0.2, 0.02], first.Bbox);
        Assert.Equal(["Valor", "Líquido", "4.870,35"], first.Words.Select(w => w.Text));
        Assert.Equal(97.12, page.Lines[1].Confidence);
    }

    [Fact]
    public void ToPage_ToleratesBlocksWithoutGeometryOrChildren()
    {
        Block[] blocks = [new() { Id = "l1", BlockType = BlockType.LINE, Text = "sem posição" }];

        var line = Assert.Single(TextractMapper.ToPage(blocks, 1).Lines);

        Assert.Equal([0d, 0d, 0d, 0d], line.Bbox);
        Assert.Empty(line.Words);
        Assert.Equal(0, line.Confidence);
    }

    [Fact]
    public void OcrJson_UsesSnakeCaseAndRoundTrips()
    {
        var document = new OcrDocument([new OcrPage(1, [new OcrLine("Folha Mensal", 99.5, [0.1, 0.2, 0.3, 0.04], [new OcrWord("Folha", 99.4, [0.1, 0.2, 0.1, 0.04])])])]);

        var json = OcrJson.Serialize(document);
        var root = JsonDocument.Parse(json).RootElement;

        Assert.Equal("Folha Mensal", root.GetProperty("pages")[0].GetProperty("lines")[0].GetProperty("text").GetString());
        Assert.Equal(4, root.GetProperty("pages")[0].GetProperty("lines")[0].GetProperty("bbox").GetArrayLength());
        var back = OcrJson.Deserialize(json);
        Assert.Equal("Folha", back.Pages[0].Lines[0].Words[0].Text);
    }

    private static Block Line(string id, string text, float confidence, float left, float top, List<string> children) => new()
    {
        Id = id,
        BlockType = BlockType.LINE,
        Text = text,
        Confidence = confidence,
        Geometry = new Geometry { BoundingBox = new BoundingBox { Left = left, Top = top, Width = 0.2f, Height = 0.02f } },
        Relationships = [new Relationship { Type = RelationshipType.CHILD, Ids = children }],
    };

    private static Block Word(string id, string text, float confidence, float left) => new()
    {
        Id = id,
        BlockType = BlockType.WORD,
        Text = text,
        Confidence = confidence,
        Geometry = new Geometry { BoundingBox = new BoundingBox { Left = left, Top = 0.81f, Width = 0.04f, Height = 0.02f } },
    };
}
