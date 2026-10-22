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

    public async Task<Document?> GetAsync(TenantId tenantId, DossierId dossierId, DocumentId documentId, CancellationToken cancellationToken)
    {
        var response = await client.GetItemAsync(new GetItemRequest
        {
            TableName = settings.TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [TableSchema.PartitionKey] = new AttributeValue(DocumentItem.PartitionKey(tenantId, dossierId)),
                [TableSchema.SortKey] = new AttributeValue($"{DocumentItem.SortKeyPrefix}{documentId}"),
            },
            ConsistentRead = true,
        }, cancellationToken);

        return response.Item is { Count: > 0 } item ? DocumentItem.FromItem(item) : null;
    }

    // Uma transação: documento e filhos entram juntos ou nenhum entra (até 100 itens; um dossiê tem no máximo 50 páginas)
    public async Task SaveClassificationAsync(Document document, IReadOnlyList<Document> children, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(children);

        await client.TransactWriteItemsAsync(new TransactWriteItemsRequest
        {
            TransactItems =
            [
                new TransactWriteItem { Put = new Put { TableName = settings.TableName, Item = DocumentItem.ToItem(document) } },
                .. children.Select(child => new TransactWriteItem { Put = new Put { TableName = settings.TableName, Item = DocumentItem.ToItem(child) } }),
            ],
        }, cancellationToken);
    }

    public async Task UpdateAsync(Document document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        // Só atualiza documento existente: um PutItem sem condição recriaria um documento apagado
        await client.PutItemAsync(new PutItemRequest
        {
            TableName = settings.TableName,
            Item = DocumentItem.ToItem(document),
            ConditionExpression = $"attribute_exists({TableSchema.PartitionKey})",
        }, cancellationToken);
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
