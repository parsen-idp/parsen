using System.Globalization;
using Parsen.Application.Pipeline;

namespace Parsen.Eval.Synthetic;

/// <summary>Peças comuns dos geradores sintéticos: linhas de OCR e formatos brasileiros.</summary>
internal static class SyntheticOcr
{
    /// <summary>
    /// Uma linha de OCR por texto, de cima para baixo, com confiança alta e posição aproximada. Cada palavra tem
    /// a própria caixa, para que a evidência por palavra funcione como no OCR real.
    /// </summary>
    public static List<OcrLine> Lines(IEnumerable<string> rows) =>
        [.. rows.Select((text, i) =>
        {
            var y = Math.Round(0.04 + (i * 0.022), 4);
            var x = 0.05;
            var words = new List<OcrWord>();
            foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var width = Math.Round(word.Length * 0.0065, 4);
                words.Add(new OcrWord(word, 99, [Math.Round(x, 4), y, width, 0.012]));
                x += width + 0.0065;
            }

            return new OcrLine(text, 99, [0.05, y, Math.Min(0.9, text.Length * 0.0065), 0.012], words);
        })];

    /// <summary>Formato brasileiro: 1.234,56.</summary>
    public static string Pt(decimal value) =>
        value.ToString("#,##0.00", CultureInfo.InvariantCulture)
            .Replace(",", "_", StringComparison.Ordinal).Replace(".", ",", StringComparison.Ordinal).Replace("_", ".", StringComparison.Ordinal);
}
