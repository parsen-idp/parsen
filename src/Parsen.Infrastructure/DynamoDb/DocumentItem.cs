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

    public static Dictionary<string, AttributeValue> ToItem(Document document)
    {
        var item = new Dictionary<string, AttributeValue>
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
            ["Status"] = new AttributeValue(StoredValues.From(document.Status)),
        };

        if (document.Type is { } type)
        {
            item["DocumentType"] = new AttributeValue(StoredValues.From(type));
        }

        if (document.Pages is { } pages)
        {
            item["FirstPage"] = new AttributeValue { N = StoredValues.From(pages.First) };
            item["LastPage"] = new AttributeValue { N = StoredValues.From(pages.Last) };
        }

        if (document.SourceDocumentId is { } source)
        {
            item["SourceDocumentId"] = new AttributeValue(source.ToString());
        }

        return item;
    }

    // Itens gravados antes da classificação (Fase 1) não têm Status: são documentos pendentes
    public static Document FromItem(Dictionary<string, AttributeValue> item) => Document.Restore(
        DocumentId.Parse(item["Id"].S),
        TenantId.From(item["TenantId"].S),
        DossierId.Parse(item["DossierId"].S),
        UploadLimits.TryParseContentType(item["ContentType"].S, out var contentType)
            ? contentType
            : throw new InvalidDataException($"Formato de documento desconhecido na tabela: '{item["ContentType"].S}'."),
        StoredValues.ToLong(item["DeclaredSizeBytes"].N),
        StoredValues.ToTimestamp(item["CreatedAt"].S),
        item.TryGetValue("Status", out var status) ? StoredValues.ToDocumentStatus(status.S) : DocumentStatus.Pending,
        item.TryGetValue("DocumentType", out var type) ? StoredValues.ToDocumentType(type.S) : null,
        item.TryGetValue("FirstPage", out var first) && item.TryGetValue("LastPage", out var last)
            ? new PageRange((int)StoredValues.ToLong(first.N), (int)StoredValues.ToLong(last.N))
            : null,
        item.TryGetValue("SourceDocumentId", out var source) ? DocumentId.Parse(source.S) : null);
}
