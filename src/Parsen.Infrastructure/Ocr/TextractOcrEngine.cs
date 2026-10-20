using Amazon.Textract;
using Amazon.Textract.Model;
using Parsen.Application.Pipeline;

namespace Parsen.Infrastructure.Ocr;

/// <summary>
/// OCR de uma página com o Textract (DetectDocumentText), enviando os bytes do PNG. O cliente é criado
/// para us-east-1: o Textract não existe em sa-east-1 (spec, seção 6).
/// </summary>
public sealed class TextractOcrEngine(IAmazonTextract textract) : IOcrEngine
{
    public async Task<OcrPage> RecognizeAsync(byte[] png, int pageNumber, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new MemoryStream(png);
            var response = await textract.DetectDocumentTextAsync(new DetectDocumentTextRequest
            {
                Document = new Document { Bytes = stream },
            }, cancellationToken);

            return TextractMapper.ToPage(response.Blocks ?? [], pageNumber);
        }
        catch (Exception ex) when (ex is BadDocumentException or UnsupportedDocumentException or DocumentTooLargeException)
        {
            // Imagem que o Textract não aceita não melhora com nova tentativa
            throw new DocumentRejectedException("OCR_DOCUMENTO_INVALIDO", $"O OCR não aceitou a página {pageNumber}.");
        }
    }
}

/// <summary>Converte os blocos do Textract (PAGE, LINE, WORD) no modelo de OCR da Parsen.</summary>
public static class TextractMapper
{
    public static OcrPage ToPage(IReadOnlyList<Block> blocks, int pageNumber)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        var byId = blocks.Where(b => b.Id is not null).ToDictionary(b => b.Id);

        // O Textract devolve as linhas em ordem de leitura; as palavras de cada linha vêm pelas relações CHILD
        var lines = blocks
            .Where(b => b.BlockType == BlockType.LINE)
            .Select(line => new OcrLine(
                line.Text ?? string.Empty,
                Confidence(line),
                Box(line),
                [.. ChildWords(line, byId).Select(word => new OcrWord(word.Text ?? string.Empty, Confidence(word), Box(word)))]))
            .ToList();

        return new OcrPage(pageNumber, lines);
    }

    private static IEnumerable<Block> ChildWords(Block line, Dictionary<string, Block> byId) =>
        (line.Relationships ?? [])
            .Where(r => r.Type == RelationshipType.CHILD)
            .SelectMany(r => r.Ids ?? [])
            .Select(id => byId.GetValueOrDefault(id))
            .OfType<Block>()
            .Where(b => b.BlockType == BlockType.WORD);

    private static double Confidence(Block block) => Math.Round(block.Confidence ?? 0, 2);

    private static double[] Box(Block block)
    {
        var box = block.Geometry?.BoundingBox;
        return [Round(box?.Left), Round(box?.Top), Round(box?.Width), Round(box?.Height)];
    }

    private static double Round(float? value) => Math.Round(value ?? 0, 4);
}
