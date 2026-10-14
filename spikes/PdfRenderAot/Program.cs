// Spike A: PDFium (via PDFtoImage) funciona compilado em Native AOT no Amazon Linux 2023?
// Uso: PdfRenderAot <arquivo.pdf> <pasta-de-saida>
using System.Diagnostics;
using PDFtoImage;

if (args.Length != 2)
{
    Console.Error.WriteLine("Uso: PdfRenderAot <arquivo.pdf> <pasta-de-saida>");
    return 1;
}

var (pdfPath, outputDir) = (args[0], args[1]);
Directory.CreateDirectory(outputDir);

var total = Stopwatch.StartNew();
var pdf = File.ReadAllBytes(pdfPath);
var pageCount = Conversion.GetPageCount(pdf);
Console.WriteLine($"Páginas: {pageCount}");

// 150 DPI: legível para Textract e Claude sem gerar imagens enormes
var options = new RenderOptions(Dpi: 150);

for (var page = 0; page < pageCount; page++)
{
    var pageTime = Stopwatch.StartNew();
    var pngPath = Path.Combine(outputDir, $"page-{page + 1}.png");
    Conversion.SavePng(pngPath, pdf, page: page, options: options);
    Console.WriteLine($"Página {page + 1}: {new FileInfo(pngPath).Length / 1024} KB em {pageTime.ElapsedMilliseconds} ms");
}

Console.WriteLine($"Total: {total.ElapsedMilliseconds} ms");
return 0;
