using Parsen.Domain.Tenants;

namespace Parsen.Domain.Dossiers;

/// <summary>
/// Documento de um dossiê. Nasce quando o cliente pede a URL de upload; o arquivo vai direto ao S3.
/// O nome original do arquivo não é guardado (pode conter dados pessoais, regra 3).
/// Um arquivo com vários documentos é dividido (split): ele vira arquivo de origem e cada documento
/// vira um filho, com páginas e OCR do arquivo de origem (spec, seção 14).
/// </summary>
public sealed class Document
{
    private Document(
        DocumentId id,
        TenantId tenantId,
        DossierId dossierId,
        DocumentContentType contentType,
        long declaredSizeBytes,
        DateTimeOffset createdAt,
        DocumentStatus status,
        DocumentType? type,
        PageRange? pages,
        DocumentId? sourceDocumentId)
    {
        Id = id;
        TenantId = tenantId;
        DossierId = dossierId;
        ContentType = contentType;
        DeclaredSizeBytes = declaredSizeBytes;
        CreatedAt = createdAt;
        Status = status;
        Type = type;
        Pages = pages;
        SourceDocumentId = sourceDocumentId;
    }

    public DocumentId Id { get; }

    public TenantId TenantId { get; }

    public DossierId DossierId { get; }

    public DocumentContentType ContentType { get; }

    /// <summary>Tamanho informado pelo cliente; o real é conferido no S3 no submit. Zero nos filhos de um split.</summary>
    public long DeclaredSizeBytes { get; }

    public DateTimeOffset CreatedAt { get; }

    public DocumentStatus Status { get; private set; }

    /// <summary>Tipo do documento; null antes da classificação e no arquivo de origem de um split.</summary>
    public DocumentType? Type { get; private set; }

    /// <summary>Páginas do documento no arquivo enviado; null antes da classificação e no arquivo de origem.</summary>
    public PageRange? Pages { get; private set; }

    /// <summary>Arquivo de onde o documento saiu, nos filhos de um split (páginas, OCR e original ficam lá).</summary>
    public DocumentId? SourceDocumentId { get; }

    /// <summary>Documento cujos arquivos (original, páginas, textract.json) estão no S3: ele mesmo ou o arquivo de origem.</summary>
    public DocumentId FileId => SourceDocumentId ?? Id;

    public bool IsExtractable => Status == DocumentStatus.Classified && Type is not DocumentType.Outro;

    public static Document Create(TenantId tenantId, DossierId dossierId, DocumentContentType contentType, long declaredSizeBytes, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(declaredSizeBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(declaredSizeBytes, UploadLimits.MaxFileSizeBytes);

        return new Document(DocumentId.New(), tenantId, dossierId, contentType, declaredSizeBytes, clock.GetUtcNow(),
            DocumentStatus.Pending, null, null, null);
    }

    /// <summary>Reconstrói um documento já persistido.</summary>
    public static Document Restore(
        DocumentId id,
        TenantId tenantId,
        DossierId dossierId,
        DocumentContentType contentType,
        long declaredSizeBytes,
        DateTimeOffset createdAt,
        DocumentStatus status = DocumentStatus.Pending,
        DocumentType? type = null,
        PageRange? pages = null,
        DocumentId? sourceDocumentId = null) =>
        new(id, tenantId, dossierId, contentType, declaredSizeBytes, createdAt, status, type, pages, sourceDocumentId);

    /// <summary>O arquivo é um documento só: este mesmo documento recebe tipo e páginas.</summary>
    public void Classify(DocumentType type, PageRange pages)
    {
        EnsureStatus(nameof(Classify), DocumentStatus.Pending);
        Type = type;
        Pages = pages;
        Status = DocumentStatus.Classified;
    }

    /// <summary>
    /// O arquivo tem dois ou mais documentos: vira arquivo de origem e devolve os filhos, já classificados.
    /// As faixas vêm em ordem e não se sobrepõem; páginas sem documento (ex.: em branco) podem ficar de fora.
    /// </summary>
    public IReadOnlyList<Document> Split(IReadOnlyList<(DocumentType Type, PageRange Pages)> parts, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureStatus(nameof(Split), DocumentStatus.Pending);

        if (parts.Count < 2)
        {
            throw new ArgumentException("O split tem pelo menos dois documentos; um documento só usa Classify.", nameof(parts));
        }

        for (var i = 1; i < parts.Count; i++)
        {
            if (parts[i].Pages.First <= parts[i - 1].Pages.Last)
            {
                throw new ArgumentException("As faixas de páginas do split precisam estar em ordem e sem sobreposição.", nameof(parts));
            }
        }

        Status = DocumentStatus.Split;
        var now = clock.GetUtcNow();
        return [.. parts.Select(p => new Document(DocumentId.New(), TenantId, DossierId, ContentType, 0, now,
            DocumentStatus.Classified, p.Type, p.Pages, Id))];
    }

    public void MarkExtracted()
    {
        EnsureStatus(nameof(MarkExtracted), DocumentStatus.Classified);
        if (Type == DocumentType.Outro)
        {
            throw new InvalidDocumentTransitionException(Status, nameof(MarkExtracted));
        }

        Status = DocumentStatus.Extracted;
    }

    private void EnsureStatus(string action, DocumentStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidDocumentTransitionException(Status, action);
        }
    }
}
