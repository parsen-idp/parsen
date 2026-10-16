using System.Text.Json.Nodes;

namespace Parsen.Auth.PreTokenGeneration;

/// <summary>
/// Acrescenta o claim tenant_id aos tokens (evento V3 do gatilho Pre Token Generation).
/// Sem tenant identificável, lança exceção: o Cognito não emite o token.
/// </summary>
public sealed class TokenEnricher(ITenantLookup lookup)
{
    public const string TenantClaim = "tenant_id";
    public const string ClientCredentialsTrigger = "TokenGeneration_ClientCredentials";

    public async Task<JsonNode> EnrichAsync(JsonNode cognitoEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cognitoEvent);

        var isMachine = (string?)cognitoEvent["triggerSource"] == ClientCredentialsTrigger;
        var tenantId = isMachine
            ? await FindMachineTenantAsync(cognitoEvent, cancellationToken)
            : (string?)cognitoEvent["request"]?["userAttributes"]?["custom:tenant_id"];

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            // Mensagem genérica: o Cognito pode repassá-la a quem pediu o token
            throw new TenantNotFoundException("Credencial sem tenant associado.");
        }

        var details = new JsonObject
        {
            ["accessTokenGeneration"] = Claims(tenantId),
        };

        if (!isMachine)
        {
            details["idTokenGeneration"] = Claims(tenantId);

            // Sem repetir os grupos na resposta, o Cognito remove cognito:groups (papéis do backoffice)
            details["groupOverrideDetails"] = cognitoEvent["request"]?["groupConfiguration"]?.DeepClone();
        }

        cognitoEvent["response"] = new JsonObject { ["claimsAndScopeOverrideDetails"] = details };
        return cognitoEvent;
    }

    private async Task<string?> FindMachineTenantAsync(JsonNode cognitoEvent, CancellationToken cancellationToken)
    {
        var clientId = (string?)cognitoEvent["callerContext"]?["clientId"];
        return string.IsNullOrWhiteSpace(clientId)
            ? null
            : await lookup.FindTenantForClientAsync(clientId, cancellationToken);
    }

    private static JsonObject Claims(string tenantId) => new()
    {
        ["claimsToAddOrOverride"] = new JsonObject { [TenantClaim] = tenantId },
    };
}

public sealed class TenantNotFoundException(string message) : Exception(message);
