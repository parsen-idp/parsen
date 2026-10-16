using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace Parsen.Auth.PreTokenGeneration;

/// <summary>Descobre o tenant dono de um app client de integração (client credentials).</summary>
public interface ITenantLookup
{
    Task<string?> FindTenantForClientAsync(string clientId, CancellationToken cancellationToken);
}

/// <summary>
/// Lê o item de plataforma APICLIENT#{clientId} / META (spec, seção 9). A role desta Lambda só
/// alcança chaves com esse prefixo, nunca dados de tenant.
/// </summary>
public sealed class DynamoDbTenantLookup(IAmazonDynamoDB client, string tableName) : ITenantLookup
{
    public async Task<string?> FindTenantForClientAsync(string clientId, CancellationToken cancellationToken)
    {
        var response = await client.GetItemAsync(new GetItemRequest
        {
            TableName = tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new($"APICLIENT#{clientId}"),
                ["SK"] = new("META"),
            },
            ProjectionExpression = "TenantId",
        }, cancellationToken);

        return response.Item is { Count: > 0 } item && item.TryGetValue("TenantId", out var tenant) ? tenant.S : null;
    }
}
