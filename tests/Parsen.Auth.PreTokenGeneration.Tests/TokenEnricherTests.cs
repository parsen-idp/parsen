using System.Text.Json.Nodes;

namespace Parsen.Auth.PreTokenGeneration.Tests;

public sealed class TokenEnricherTests
{
    private readonly FakeTenantLookup _lookup = new();
    private readonly TokenEnricher _enricher;

    public TokenEnricherTests() => _enricher = new TokenEnricher(_lookup);

    // ---------- Usuário do backoffice ----------

    [Fact]
    public async Task User_WithTenantAttribute_AddsTenantToAccessAndIdTokens()
    {
        var result = await _enricher.EnrichAsync(UserEvent(tenantId: "ten_a"), CancellationToken.None);

        Assert.Equal("ten_a", (string?)Details(result)["accessTokenGeneration"]!["claimsToAddOrOverride"]!["tenant_id"]);
        Assert.Equal("ten_a", (string?)Details(result)["idTokenGeneration"]!["claimsToAddOrOverride"]!["tenant_id"]);
    }

    [Fact]
    public async Task User_KeepsGroupsSoRolesStayInToken()
    {
        var result = await _enricher.EnrichAsync(UserEvent(tenantId: "ten_a"), CancellationToken.None);

        var groups = Details(result)["groupOverrideDetails"]!["groupsToOverride"]!.AsArray();
        Assert.Equal(["analista"], groups.Select(g => (string?)g));
    }

    [Fact]
    public async Task User_WithoutTenantAttribute_Throws()
    {
        await Assert.ThrowsAsync<TenantNotFoundException>(
            () => _enricher.EnrichAsync(UserEvent(tenantId: null), CancellationToken.None));
    }

    // ---------- Integração de fintech (client credentials) ----------

    [Fact]
    public async Task Machine_KnownClient_AddsTenantToAccessTokenOnly()
    {
        _lookup.Clients["client-123"] = "ten_b";

        var result = await _enricher.EnrichAsync(MachineEvent("client-123"), CancellationToken.None);

        Assert.Equal("ten_b", (string?)Details(result)["accessTokenGeneration"]!["claimsToAddOrOverride"]!["tenant_id"]);
        Assert.Null(Details(result)["idTokenGeneration"]);
        Assert.Null(Details(result)["groupOverrideDetails"]);
    }

    [Fact]
    public async Task Machine_UnknownClient_Throws()
    {
        await Assert.ThrowsAsync<TenantNotFoundException>(
            () => _enricher.EnrichAsync(MachineEvent("client-desconhecido"), CancellationToken.None));
    }

    [Fact]
    public async Task Machine_IgnoresTenantSentByTheClient()
    {
        // O cliente não escolhe o próprio tenant: só o mapeamento APICLIENT# vale
        var evt = MachineEvent("client-sem-mapeamento");
        evt["request"]!["clientMetadata"] = new JsonObject { ["tenant_id"] = "ten_forjado" };

        await Assert.ThrowsAsync<TenantNotFoundException>(() => _enricher.EnrichAsync(evt, CancellationToken.None));
    }

    // ---------- Auxiliares ----------

    private static JsonNode Details(JsonNode result) => result["response"]!["claimsAndScopeOverrideDetails"]!;

    private static JsonObject UserEvent(string? tenantId)
    {
        var attributes = new JsonObject { ["sub"] = "usr-1", ["email"] = "analista@exemplo.com.br" };
        if (tenantId is not null)
        {
            attributes["custom:tenant_id"] = tenantId;
        }

        return new JsonObject
        {
            ["version"] = "3",
            ["triggerSource"] = "TokenGeneration_Authentication",
            ["callerContext"] = new JsonObject { ["clientId"] = "backoffice" },
            ["request"] = new JsonObject
            {
                ["userAttributes"] = attributes,
                ["groupConfiguration"] = new JsonObject
                {
                    ["groupsToOverride"] = new JsonArray("analista"),
                    ["iamRolesToOverride"] = new JsonArray(),
                },
            },
            ["response"] = new JsonObject(),
        };
    }

    private static JsonObject MachineEvent(string clientId) => new JsonObject
    {
        ["version"] = "3",
        ["triggerSource"] = TokenEnricher.ClientCredentialsTrigger,
        ["callerContext"] = new JsonObject { ["clientId"] = clientId },
        ["request"] = new JsonObject { ["userAttributes"] = new JsonObject(), ["scopes"] = new JsonArray("parsen/dossiers.read") },
        ["response"] = new JsonObject(),
    };

    private sealed class FakeTenantLookup : ITenantLookup
    {
        public Dictionary<string, string> Clients { get; } = [];

        public Task<string?> FindTenantForClientAsync(string clientId, CancellationToken cancellationToken) =>
            Task.FromResult(Clients.GetValueOrDefault(clientId));
    }
}
