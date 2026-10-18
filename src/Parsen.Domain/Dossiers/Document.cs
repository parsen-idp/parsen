using Parsen.Domain.Tenants;

namespace Parsen.Domain.Dossiers;

/// <summary>
/// Documento de um dossiê. Nasce quando o cliente pede a URL de upload; o arquivo vai direto ao S3.
/// O nome original do arquivo não é guardado (pode conter dados pessoais, regra 3).
/// </summary>
public sealed class Document
{
    private Document(DocumentId id, TenantId tenantId, DossierId dossierId, DocumentContentType contentType, long declaredSizeBytes, DateTimeOffset createdAt)
    {
        Id = id;
        TenantId = tenantId;
        DossierId = dossierId;
        ContentType = contentType;
        DeclaredSizeBytes = declaredSizeBytes;
        CreatedAt = createdAt;
    }

    public DocumentId Id { get; }

    public TenantId TenantId { get; }

    public DossierId DossierId { get; }

    public DocumentContentType ContentType { get; }

    /// <summary>Tamanho informado pelo cliente; o real é conferido no S3 no submit.</summary>
    public long DeclaredSizeBytes { get; }

    public DateTimeOffset CreatedAt { get; }

    public static Document Create(TenantId tenantId, DossierId dossierId, DocumentContentType contentType, long declaredSizeBytes, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(declaredSizeBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(declaredSizeBytes, UploadLimits.MaxFileSizeBytes);

        return new Document(DocumentId.New(), tenantId, dossierId, contentType, declaredSizeBytes, clock.GetUtcNow());
    }

    /// <summary>Reconstrói um documento já persistido.</summary>
    public static Document Restore(DocumentId id, TenantId tenantId, DossierId dossierId, DocumentContentType contentType, long declaredSizeBytes, DateTimeOffset createdAt) =>
        new(id, tenantId, dossierId, contentType, declaredSizeBytes, createdAt);
}
