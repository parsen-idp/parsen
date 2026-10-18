using Amazon.DynamoDBv2.Model;
using Parsen.Domain.Dossiers;
using Parsen.Domain.Tenants;

namespace Parsen.Infrastructure.DynamoDb;

/// <summary>
/// Documento como item da tabela única: mora na "gaveta" do dossiê (spec, seção 9), então uma
/// Query traz todos os documentos de um dossiê de uma vez, em ordem de criação (ULID).
/// </summary>
internal static class DocumentItem
{
    public const string EntityType = "Document";
    public const string SortKeyPrefix = "DOC#";

    public static string PartitionKey(TenantId tenantId, DossierId dossierId) => $"TENANT#{tenantId}#DOSSIER#{dossierId}";

    public static Dictionary<string, AttributeValue> ToItem(Document document) => new()
    {
        [TableSchema.PartitionKey] = new AttributeValue(PartitionKey(document.TenantId, document.DossierId)),
        [TableSchema.SortKey] = new AttributeValue($"{SortKeyPrefix}{document.Id}"),
        ["EntityType"] = new AttributeValue(EntityType),
        ["Id"] = new AttributeValue(document.Id.ToString()),
        ["TenantId"] = new AttributeValue(document.TenantId.Value),
        ["DossierId"] = new AttributeValue(document.DossierId.ToString()),
        ["ContentType"] = new AttributeValue(UploadLimits.ToMimeType(document.ContentType)),
        ["DeclaredSizeBytes"] = new AttributeValue { N = StoredValues.From(document.DeclaredSizeBytes) },
        ["CreatedAt"] = new AttributeValue(StoredValues.From(document.CreatedAt)),
    };

    public static Document FromItem(Dictionary<string, AttributeValue> item) => Document.Restore(
        DocumentId.Parse(item["Id"].S),
        TenantId.From(item["TenantId"].S),
        DossierId.Parse(item["DossierId"].S),
        UploadLimits.TryParseContentType(item["ContentType"].S, out var contentType)
            ? contentType
            : throw new InvalidDataException($"Formato de documento desconhecido na tabela: '{item["ContentType"].S}'."),
        StoredValues.ToLong(item["DeclaredSizeBytes"].N),
        StoredValues.ToTimestamp(item["CreatedAt"].S));
}
