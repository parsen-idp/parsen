using Parsen.Domain.Dossiers;
using Parsen.Domain.Tenants;

namespace Parsen.Application.Documents;

/// <summary>Porta de persistência dos documentos de um dossiê.</summary>
public interface IDocumentRepository
{
    Task AddAsync(Document document, CancellationToken cancellationToken);

    Task<IReadOnlyList<Document>> ListByDossierAsync(TenantId tenantId, DossierId dossierId, CancellationToken cancellationToken);
}

/// <summary>
/// Armazenamento dos arquivos (S3 na infraestrutura). O caminho do arquivo é decidido pela
/// implementação a partir do documento, sempre sob o prefixo do tenant.
/// </summary>
public interface IFileStorage
{
    /// <summary>Autoriza um upload direto do cliente: URL temporária e cabeçalhos que ele deve enviar.</summary>
    Task<UploadTicket> CreateUploadAsync(Document document, CancellationToken cancellationToken);

    /// <summary>Dados do arquivo já enviado, ou null se ele ainda não chegou.</summary>
    Task<StoredFile?> GetStoredFileAsync(Document document, CancellationToken cancellationToken);
}

public sealed record UploadTicket(Uri Url, string Method, IReadOnlyDictionary<string, string> Headers, DateTimeOffset ExpiresAt);

public sealed record StoredFile(long SizeBytes, string ContentType);
