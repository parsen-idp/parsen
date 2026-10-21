using System.Globalization;
using Amazon.DynamoDBv2.Model;
using Parsen.Application.Pipeline;
using Parsen.Domain.Dossiers;
using Parsen.Domain.Tenants;

namespace Parsen.Infrastructure.DynamoDb;

/// <summary>
/// Extração como itens da tabela única (spec, seção 9): partição TENANT#{t}#DOC#{doc}, com o detalhe
/// do documento em META e um item por campo (FIELD#{nome}) e por linha de lista (FIELD#{lista}#{ordem}).
/// </summary>
internal static class ExtractionItems
{
    public const string DetailSortKey = "META";
    public const string FieldSortKeyPrefix = "FIELD#";

    public static string PartitionKey(TenantId tenantId, DocumentId documentId) => $"TENANT#{tenantId}#DOC#{documentId}";

    public static Dictionary<string, AttributeValue> Detail(Document document, ExtractionResult result) => new()
    {
        [TableSchema.PartitionKey] = new AttributeValue(PartitionKey(document.TenantId, document.Id)),
        [TableSchema.SortKey] = new AttributeValue(DetailSortKey),
        ["EntityType"] = new AttributeValue("DocumentDetail"),
        ["DocumentId"] = new AttributeValue(document.Id.ToString()),
        ["DossierId"] = new AttributeValue(document.DossierId.ToString()),
        ["DocumentType"] = new AttributeValue(result.DocumentType),
        ["SchemaId"] = new AttributeValue(result.SchemaId),
        ["ModelId"] = new AttributeValue(result.ModelId),
        ["Alerts"] = new AttributeValue { L = [.. result.Alerts.Select(a => new AttributeValue(a))] },
        ["Usage"] = new AttributeValue
        {
            M = new Dictionary<string, AttributeValue>
            {
                ["InputTokens"] = Integer(result.Usage.InputTokens),
                ["OutputTokens"] = Integer(result.Usage.OutputTokens),
                ["CacheReadTokens"] = Integer(result.Usage.CacheReadTokens),
                ["CacheWriteTokens"] = Integer(result.Usage.CacheWriteTokens),
            },
        },
        ["ExtractedAt"] = new AttributeValue(StoredValues.From(result.ExtractedAt)),
    };

    public static Dictionary<string, AttributeValue> Field(Document document, ExtractedField field)
    {
        var item = FieldBase(document, $"{FieldSortKeyPrefix}{field.Name}", "ExtractedField");
        item["Name"] = new AttributeValue(field.Name);
        item["Value"] = ToAttribute(field.Value);
        if (field.Evidence is not null)
        {
            item["Evidence"] = ToAttribute(field.Evidence);
            item["Confidence"] = Number(field.Confidence!.Value);
        }

        return item;
    }

    public static Dictionary<string, AttributeValue> ListItem(Document document, string listName, int order, ExtractedItem listItem)
    {
        // Ordem com 3 dígitos: a SK ordena como texto e a Query devolve as linhas na ordem do documento
        var item = FieldBase(document, $"{FieldSortKeyPrefix}{listName}#{order.ToString("D3", CultureInfo.InvariantCulture)}", "ExtractedListItem");
        item["List"] = new AttributeValue(listName);
        item["Order"] = new AttributeValue { N = order.ToString(CultureInfo.InvariantCulture) };
        item["Attributes"] = new AttributeValue { M = listItem.Attributes.ToDictionary(a => a.Key, a => ToAttribute(a.Value)) };
        item["Evidence"] = ToAttribute(listItem.Evidence);
        item["Confidence"] = Number(listItem.Confidence);
        return item;
    }

    private static Dictionary<string, AttributeValue> FieldBase(Document document, string sortKey, string entityType) => new()
    {
        [TableSchema.PartitionKey] = new AttributeValue(PartitionKey(document.TenantId, document.Id)),
        [TableSchema.SortKey] = new AttributeValue(sortKey),
        ["EntityType"] = new AttributeValue(entityType),
        ["Corrected"] = new AttributeValue { BOOL = false },
    };

    // Número gravado como N com o texto exato do modelo: o DynamoDB guarda decimal sem arredondar
    private static AttributeValue ToAttribute(FieldValue? value) => value switch
    {
        null => new AttributeValue { NULL = true },
        { Kind: FieldValueKind.Number } => new AttributeValue { N = value.Raw },
        _ => new AttributeValue(value.Raw),
    };

    private static AttributeValue ToAttribute(Evidence evidence)
    {
        var map = new Dictionary<string, AttributeValue>
        {
            ["Page"] = new AttributeValue { N = evidence.Page.ToString(CultureInfo.InvariantCulture) },
            ["Text"] = new AttributeValue(evidence.Text),
        };

        if (evidence.Bbox is not null)
        {
            map["Bbox"] = new AttributeValue { L = [.. evidence.Bbox.Select(Number)] };
        }

        return new AttributeValue { M = map };
    }

    private static AttributeValue Number(double value) => new() { N = value.ToString("R", CultureInfo.InvariantCulture) };

    private static AttributeValue Integer(int value) => new() { N = value.ToString(CultureInfo.InvariantCulture) };
}
