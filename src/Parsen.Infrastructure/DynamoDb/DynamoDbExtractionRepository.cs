using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Parsen.Application.Pipeline;
using Parsen.Domain.Dossiers;

namespace Parsen.Infrastructure.DynamoDb;

/// <summary>Campos extraídos na partição TENANT#{tenant}#DOC#{doc} da tabela única.</summary>
public sealed class DynamoDbExtractionRepository(IAmazonDynamoDB client, DynamoDbSettings settings) : IExtractionRepository
{
    // Limite do BatchWriteItem por chamada
    private const int BatchSize = 25;
    private const int MaxUnprocessedRetries = 3;

    public async Task SaveAsync(Document document, ExtractionResult result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(result);

        var fields = result.Fields.Select(f => ExtractionItems.Field(document, f))
            .Concat(result.Lists.SelectMany(l => l.Value.Select((item, index) => ExtractionItems.ListItem(document, l.Key, index + 1, item))))
            .ToList();

        // Uma nova extração substitui a anterior: campos que não vieram desta vez (ex.: menos rubricas) são apagados
        var newKeys = fields.Select(f => f[TableSchema.SortKey].S).ToHashSet(StringComparer.Ordinal);
        var stale = (await ListFieldSortKeysAsync(document, cancellationToken)).Where(k => !newKeys.Contains(k));

        var writes = fields.Select(item => new WriteRequest(new PutRequest(item)))
            .Concat(stale.Select(sortKey => new WriteRequest(new DeleteRequest(Key(document, sortKey)))))
            .ToList();

        foreach (var batch in writes.Chunk(BatchSize))
        {
            await WriteBatchAsync([.. batch], cancellationToken);
        }

        // Detalhe por último: só marca o documento como extraído depois que todos os campos estão gravados
        await client.PutItemAsync(new PutItemRequest
        {
            TableName = settings.TableName,
            Item = ExtractionItems.Detail(document, result),
        }, cancellationToken);
    }

    private async Task<List<string>> ListFieldSortKeysAsync(Document document, CancellationToken cancellationToken)
    {
        var keys = new List<string>();
        Dictionary<string, AttributeValue>? lastKey = null;

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
                    [":pk"] = new AttributeValue(ExtractionItems.PartitionKey(document.TenantId, document.Id)),
                    [":prefix"] = new AttributeValue(ExtractionItems.FieldSortKeyPrefix),
                },
                ProjectionExpression = "#sk",
                ConsistentRead = true,
                ExclusiveStartKey = lastKey,
            }, cancellationToken);

            keys.AddRange((response.Items ?? []).Select(i => i[TableSchema.SortKey].S));
            lastKey = response.LastEvaluatedKey is { Count: > 0 } next ? next : null;
        }
        while (lastKey is not null);

        return keys;
    }

    private async Task WriteBatchAsync(List<WriteRequest> batch, CancellationToken cancellationToken)
    {
        var pending = new Dictionary<string, List<WriteRequest>> { [settings.TableName] = batch };

        // Sob carga o DynamoDB pode devolver parte do lote sem gravar: reenvia só o que ficou, com espera crescente
        for (var attempt = 0; ; attempt++)
        {
            var response = await client.BatchWriteItemAsync(new BatchWriteItemRequest { RequestItems = pending }, cancellationToken);
            if (response.UnprocessedItems is not { Count: > 0 } unprocessed)
            {
                return;
            }

            if (attempt == MaxUnprocessedRetries)
            {
                throw new InvalidOperationException("O DynamoDB não gravou todos os campos extraídos depois das novas tentativas.");
            }

            pending = unprocessed;
            await Task.Delay(TimeSpan.FromMilliseconds(100 * (1 << attempt)), cancellationToken);
        }
    }

    private static Dictionary<string, AttributeValue> Key(Document document, string sortKey) => new()
    {
        [TableSchema.PartitionKey] = new AttributeValue(ExtractionItems.PartitionKey(document.TenantId, document.Id)),
        [TableSchema.SortKey] = new AttributeValue(sortKey),
    };
}
