namespace Parsen.Domain.Dossiers;

/// <summary>Formatos aceitos para documentos do dossiê.</summary>
public enum DocumentContentType
{
    Pdf,
    Jpeg,
    Png,
}

/// <summary>Limites de upload da fase 1 (spec, seção 7); a revisar no piloto.</summary>
public static class UploadLimits
{
    public const long MaxFileSizeBytes = 20L * 1024 * 1024;

    public static bool TryParseContentType(string? mimeType, out DocumentContentType contentType)
    {
        DocumentContentType? parsed = mimeType?.Trim().ToUpperInvariant() switch
        {
            "APPLICATION/PDF" => DocumentContentType.Pdf,
            "IMAGE/JPEG" => DocumentContentType.Jpeg,
            "IMAGE/PNG" => DocumentContentType.Png,
            _ => null,
        };

        contentType = parsed.GetValueOrDefault();
        return parsed.HasValue;
    }

    public static string ToMimeType(DocumentContentType contentType) => contentType switch
    {
        DocumentContentType.Pdf => "application/pdf",
        DocumentContentType.Jpeg => "image/jpeg",
        DocumentContentType.Png => "image/png",
        _ => throw new ArgumentOutOfRangeException(nameof(contentType), contentType, null),
    };
}
