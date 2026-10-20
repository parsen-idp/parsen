using Parsen.Application.Pipeline;
using Parsen.Domain.Dossiers;

namespace Parsen.Workers.Tests;

/// <summary>Simula os arquivos do documento no S3 para os testes dos workers.</summary>
internal sealed class FakeDocumentFiles : IDocumentFiles
{
    public int? RetentionDays { get; set; } = 90;

    public List<(int PageNumber, int RetentionDays)> SavedPages { get; } = [];

    public List<int> ReadPages { get; } = [];

    public (OcrDocument Ocr, int RetentionDays)? SavedOcr { get; private set; }

    public Task<byte[]> ReadOriginalAsync(Document document, CancellationToken cancellationToken) => Task.FromResult(new byte[] { 1 });

    public Task<int?> GetRetentionDaysAsync(Document document, CancellationToken cancellationToken) => Task.FromResult(RetentionDays);

    public Task SavePageAsync(Document document, int pageNumber, byte[] png, int retentionDays, CancellationToken cancellationToken)
    {
        SavedPages.Add((pageNumber, retentionDays));
        return Task.CompletedTask;
    }

    public Task<byte[]> ReadPageAsync(Document document, int pageNumber, CancellationToken cancellationToken)
    {
        ReadPages.Add(pageNumber);
        return Task.FromResult(new byte[] { (byte)pageNumber });
    }

    public Task SaveOcrAsync(Document document, OcrDocument ocr, int retentionDays, CancellationToken cancellationToken)
    {
        SavedOcr = (ocr, retentionDays);
        return Task.CompletedTask;
    }
}
