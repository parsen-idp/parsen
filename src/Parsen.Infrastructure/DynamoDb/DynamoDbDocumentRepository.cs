using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Parsen.Application.Common;
using Parsen.Application.Documents;
using Parsen.Domain.Dossiers;
using Parsen.Domain.Tenants;

namespace Parsen.Infrastructure.DynamoDb;

/// <summary>Documentos na partição TENANT#{tenant}#DOSSIER#{dossier} da tabela única.</summary>
public sealed class DynamoDbDocumentRepository(IAmazonDynamoDB client, DynamoDbSettings settings) : IDocumentRepository
{
    public async Task AddAsync(Document document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        try
        {
            await client.PutItemAsync(new PutItemRequest
            {
                TableName = settings.TableName,
                Item = DocumentItem.ToItem(document),
                ConditionExpression = $"attribute_not_exists({TableSchema.PartitionKey})",
            }, cancellationToken);
        }
        catch (ConditionalCheckFailedException ex)
        {
            throw new PersistenceConflictException($"O documento {document.Id} já existe.", ex);
        }
    }

    public async Task<IReadOnlyList<Document>> ListByDossierAsync(TenantId tenantId, DossierId dossierId, CancellationToken cancellationToken)
    {
        var documents = new List<Document>();
        Dictionary<string, AttributeValue>? lastKey = null;

        // A Query devolve até 1 MB por página: segue as páginas até o fim
        do
        {
            var response = await client.QueryAsync(new QueryRequest
            {
                TableName = settings.TableName,
                KeyConditionExpression = "#pk = :pk AND begins_with(#sk, :prefix)",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    ["#pk"] = TableSchema.PartitionKey,
                    ["#sk"] = TableSchema.SortKey,
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":pk"] = new AttributeValue(DocumentItem.PartitionKey(tenantId, dossierId)),
                    [":prefix"] = new AttributeValue(DocumentItem.SortKeyPrefix),
                },

                // Leitura forte: o submit costuma vir logo depois do pedido de upload
                ConsistentRead = true,
                ExclusiveStartKey = lastKey,
            }, cancellationToken);

            documents.AddRange((response.Items ?? []).Select(DocumentItem.FromItem));
            lastKey = response.LastEvaluatedKey is { Count: > 0 } next ? next : null;
        }
        while (lastKey is not null);

        return documents;
    }
}
