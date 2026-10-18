using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Parsen.Application.Common;
using Parsen.Application.Dossiers;
using Parsen.Domain.Dossiers;
using Parsen.Domain.Tenants;

namespace Parsen.Infrastructure.DynamoDb;

/// <summary>Dossiês na tabela única, sempre sob a partição TENANT#{tenantId}.</summary>
public sealed class DynamoDbDossierRepository(IAmazonDynamoDB client, DynamoDbSettings settings) : IDossierRepository
{
    public async Task AddAsync(Dossier dossier, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dossier);

        try
        {
            await client.PutItemAsync(new PutItemRequest
            {
                TableName = settings.TableName,
                Item = DossierItem.ToItem(dossier),

                // Criação nunca sobrescreve um item existente
                ConditionExpression = $"attribute_not_exists({TableSchema.PartitionKey})",
            }, cancellationToken);
        }
        catch (ConditionalCheckFailedException ex)
        {
            throw new PersistenceConflictException($"O dossiê {dossier.Id} já existe.", ex);
        }
    }

    public async Task UpdateAsync(Dossier dossier, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dossier);

        // Grava o item inteiro (status, GSI1 da fila etc.) com a versão seguinte, mas só se ninguém
        // tiver gravado depois da nossa leitura: controle otimista de concorrência
        var item = DossierItem.ToItem(dossier);
        item["Version"] = new AttributeValue { N = StoredValues.From(dossier.Version + 1) };

        try
        {
            await client.PutItemAsync(new PutItemRequest
            {
                TableName = settings.TableName,
                Item = item,
                ConditionExpression = "#version = :expected",
                ExpressionAttributeNames = new Dictionary<string, string> { ["#version"] = "Version" },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":expected"] = new AttributeValue { N = StoredValues.From(dossier.Version) },
                },
            }, cancellationToken);
        }
        catch (ConditionalCheckFailedException ex)
        {
            throw new PersistenceConflictException($"O dossiê {dossier.Id} foi alterado por outra requisição.", ex);
        }

        dossier.MarkSaved();
    }

    public async Task<Dossier?> GetAsync(TenantId tenantId, DossierId id, CancellationToken cancellationToken)
    {
        var response = await client.GetItemAsync(new GetItemRequest
        {
            TableName = settings.TableName,
            Key = DossierItem.Key(tenantId, id),

            // Leitura forte: enxerga uma escrita feita logo antes (ex.: criar e consultar em seguida)
            ConsistentRead = true,
        }, cancellationToken);

        return response.Item is { Count: > 0 } item ? DossierItem.FromItem(item) : null;
    }
}
