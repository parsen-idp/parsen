using System.Collections.Concurrent;
using Parsen.Application.Documents;
using Parsen.Application.Dossiers;
using Parsen.Domain.Dossiers;

namespace Parsen.Api.Tests.Infrastructure;

/// <summary>Simula o S3: gera URLs falsas e deixa o teste "enviar" arquivos.</summary>
public sealed class FakeFileStorage : IFileStorage
{
    private readonly ConcurrentDictionary<string, StoredFile> _uploaded = new();

    /// <summary>Simula o PUT do cliente na URL pré-assinada.</summary>
    public void SimulateUpload(string documentId, long sizeBytes, string contentType = "application/pdf") =>
        _uploaded[documentId] = new StoredFile(sizeBytes, contentType);

    public Task<UploadTicket> CreateUploadAsync(Document document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        var url = new Uri($"https://fake-s3.local/{document.TenantId}/{document.DossierId}/{document.Id}/original");
        var headers = new Dictionary<string, string> { ["Content-Type"] = UploadLimits.ToMimeType(document.ContentType) };
        return Task.FromResult(new UploadTicket(url, "PUT", headers, ParsenApiFactory.Now.AddMinutes(15)));
    }

    public Task<StoredFile?> GetStoredFileAsync(Document document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Task.FromResult(_uploaded.GetValueOrDefault(document.Id.ToString()));
    }
}

/// <summary>Simula o EventBridge: guarda os eventos publicados e pode falhar sob demanda.</summary>
public sealed class FakeEventPublisher : IEventPublisher
{
    private readonly ConcurrentQueue<DossierSubmitted> _published = new();

    public IReadOnlyCollection<DossierSubmitted> Published => _published;

    /// <summary>Dossiê cujo próximo evento deve falhar (simula o EventBridge indisponível).</summary>
    public string? FailFor { get; set; }

    public Task PublishAsync(DossierSubmitted submitted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submitted);
        if (FailFor == submitted.DossierId.ToString())
        {
            throw new HttpRequestException("EventBridge indisponível (simulado).");
        }

        _published.Enqueue(submitted);
        return Task.CompletedTask;
    }
}
