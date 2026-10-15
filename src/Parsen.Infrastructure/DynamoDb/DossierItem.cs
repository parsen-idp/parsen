using Amazon.DynamoDBv2.Model;
using Parsen.Domain.Dossiers;
using Parsen.Domain.Tenants;

namespace Parsen.Infrastructure.DynamoDb;

/// <summary>Conversão entre <see cref="Dossier"/> e o item da tabela única (spec, seção 9).</summary>
internal static class DossierItem
{
    public const string EntityType = "Dossier";

    public static string PartitionKey(TenantId tenantId) => $"TENANT#{tenantId}";

    public static string SortKey(DossierId id) => $"DOSSIER#{id}";

    public static Dictionary<string, AttributeValue> Key(TenantId tenantId, DossierId id) => new()
    {
        [TableSchema.PartitionKey] = new AttributeValue(PartitionKey(tenantId)),
        [TableSchema.SortKey] = new AttributeValue(SortKey(id)),
    };

    public static Dictionary<string, AttributeValue> ToItem(Dossier dossier)
    {
        var createdAt = StoredValues.From(dossier.CreatedAt);
        var status = StoredValues.From(dossier.Status);

        var item = Key(dossier.TenantId, dossier.Id);
        item["EntityType"] = new AttributeValue(EntityType);
        item["Id"] = new AttributeValue(dossier.Id.ToString());
        item["TenantId"] = new AttributeValue(dossier.TenantId.Value);
        item["AnalysisType"] = new AttributeValue(StoredValues.From(dossier.AnalysisType));
        item["Status"] = new AttributeValue(status);
        item["CreatedAt"] = new AttributeValue(createdAt);
        item["UpdatedAt"] = new AttributeValue(StoredValues.From(dossier.UpdatedAt));
        item["Version"] = new AttributeValue { N = StoredValues.From(dossier.Version) };

        // GSI1: fila do backoffice por status, ordenada por data (o ID desempata)
        item[TableSchema.Gsi1PartitionKey] = new AttributeValue($"TENANT#{dossier.TenantId}#STATUS#{status}");
        item[TableSchema.Gsi1SortKey] = new AttributeValue($"{createdAt}#{dossier.Id}");

        // Atributos opcionais só são gravados com valor; sem ExternalRef, o item fica fora do GSI2
        if (dossier.ExternalRef is { } externalRef)
        {
            item["ExternalRef"] = new AttributeValue(externalRef);
            item[TableSchema.Gsi2PartitionKey] = new AttributeValue($"TENANT#{dossier.TenantId}#REF#{externalRef}");
            item[TableSchema.Gsi2SortKey] = new AttributeValue(SortKey(dossier.Id));
        }

        if (dossier.DecidedBy is { } decidedBy)
        {
            item["DecidedBy"] = new AttributeValue(decidedBy);
        }

        if (dossier.RejectionReason is { } reason)
        {
            item["RejectionReason"] = new AttributeValue(reason);
        }

        return item;
    }

    public static Dossier FromItem(Dictionary<string, AttributeValue> item) => Dossier.Restore(
        DossierId.Parse(item["Id"].S),
        TenantId.From(item["TenantId"].S),
        StoredValues.ToAnalysisType(item["AnalysisType"].S),
        Optional(item, "ExternalRef"),
        StoredValues.ToDossierStatus(item["Status"].S),
        StoredValues.ToTimestamp(item["CreatedAt"].S),
        StoredValues.ToTimestamp(item["UpdatedAt"].S),
        Optional(item, "DecidedBy"),
        Optional(item, "RejectionReason"),
        StoredValues.ToLong(item["Version"].N));

    private static string? Optional(Dictionary<string, AttributeValue> item, string name) =>
        item.TryGetValue(name, out var value) ? value.S : null;
}
