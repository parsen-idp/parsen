using Parsen.Application.Common;
using Parsen.Application.Documents;
using Parsen.Application.Dossiers;
using Parsen.Domain.Dossiers;

namespace Parsen.Infrastructure;

/// <summary>
/// Usadas quando Storage:* ou Events:* não estão configurados (ex.: API local em memória): a API
/// sobe normalmente e só as operações que dependem deles respondem 503 com um código claro.
/// </summary>
internal sealed class NotConfiguredFileStorage : IFileStorage
{
    public Task<UploadTicket> CreateUploadAsync(Document document, CancellationToken cancellationToken) => throw NotConfigured("Storage");

    public Task<StoredFile?> GetStoredFileAsync(Document document, CancellationToken cancellationToken) => throw NotConfigured("Storage");

    internal static RequestRejectedException NotConfigured(string section) => new(
        "INTEGRACAO_NAO_CONFIGURADA", $"Integração não configurada neste ambiente ({section}).", RejectionKind.Unavailable);
}

internal sealed class NotConfiguredEventPublisher : IEventPublisher
{
    public Task PublishAsync(DossierSubmitted submitted, CancellationToken cancellationToken) =>
        throw NotConfiguredFileStorage.NotConfigured("Events");
}
