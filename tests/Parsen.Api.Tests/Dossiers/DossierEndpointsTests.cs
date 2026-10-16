using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Parsen.Api.Tests.Infrastructure;

namespace Parsen.Api.Tests.Dossiers;

public sealed class DossierEndpointsTests(ParsenApiFactory factory) : IClassFixture<ParsenApiFactory>
{
    private const string TenantA = "ten_a";
    private const string TenantB = "ten_b";

    // ---------- Criação ----------

    [Fact]
    public async Task Create_ValidRequest_Returns201WithLocationAndDraft()
    {
        var client = factory.CreateClientFor(TenantA);

        var response = await client.PostAsJsonAsync("/v1/dossiers", new { analysis_type = "income", external_ref = "proposta-42" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        var id = body.GetProperty("id").GetString();
        Assert.StartsWith("dos_", id, StringComparison.Ordinal);
        Assert.Equal($"/v1/dossiers/{id}", response.Headers.Location?.OriginalString);
        Assert.Equal("income", body.GetProperty("analysis_type").GetString());
        Assert.Equal("draft", body.GetProperty("status").GetString());
        Assert.Equal("proposta-42", body.GetProperty("external_ref").GetString());
        Assert.Equal("2026-10-15T12:00:00Z", body.GetProperty("created_at").GetString());
    }

    [Fact]
    public async Task CreateThenGet_ReturnsSameDossier()
    {
        var client = factory.CreateClientFor(TenantA);
        var created = await ReadJsonAsync(await client.PostAsJsonAsync("/v1/dossiers", new { analysis_type = "property" }));
        var id = created.GetProperty("id").GetString();

        var response = await client.GetAsync($"/v1/dossiers/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(id, body.GetProperty("id").GetString());
        Assert.Equal("property", body.GetProperty("analysis_type").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("external_ref").ValueKind);
    }

    // ---------- Validação ----------

    [Theory]
    [InlineData("""{ "analysis_type": "nao_existe" }""", "REQUISICAO_INVALIDA")]
    [InlineData("""{ "analysis_type": 1 }""", "REQUISICAO_INVALIDA")]
    [InlineData("""{ }""", "REQUISICAO_INVALIDA")]
    [InlineData("""{ "analysis_type": "income_check" }""", "TIPO_ANALISE_NAO_PERMITIDO")]
    public async Task Create_InvalidAnalysisType_Returns400ProblemDetails(string json, string expectedCode)
    {
        var client = factory.CreateClientFor(TenantA);

        var response = await client.PostAsync("/v1/dossiers", new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(expectedCode, (await ReadJsonAsync(response)).GetProperty("code").GetString());
    }

    // ---------- Busca e isolamento entre tenants ----------

    [Theory]
    [InlineData("nao-e-um-id")]
    [InlineData("dos_01JABCDEFGHJKMNPQRSTVWXYZ0")]
    public async Task Get_UnknownOrMalformedId_Returns404(string id)
    {
        var client = factory.CreateClientFor(TenantA);

        var response = await client.GetAsync($"/v1/dossiers/{id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("DOSSIE_NAO_ENCONTRADO", (await ReadJsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Get_OtherTenantsDossier_Returns404()
    {
        var created = await ReadJsonAsync(
            await factory.CreateClientFor(TenantA).PostAsJsonAsync("/v1/dossiers", new { analysis_type = "income" }));
        var id = created.GetProperty("id").GetString();

        var response = await factory.CreateClientFor(TenantB).GetAsync($"/v1/dossiers/{id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- Autenticação ----------

    [Fact]
    public async Task NoToken_Returns401()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/dossiers", new { analysis_type = "income" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TokenWithoutTenant_Returns401()
    {
        var client = factory.CreateClientFor(TestAuthHandler.NoTenant);

        var response = await client.PostAsJsonAsync("/v1/dossiers", new { analysis_type = "income" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("TENANT_AUSENTE", (await ReadJsonAsync(response)).GetProperty("code").GetString());
    }

    // ---------- Scopes ----------

    [Fact]
    public async Task Create_WithOnlyReadScope_Returns403()
    {
        var client = factory.CreateClientFor(TenantA, scopes: "parsen/dossiers.read");

        var response = await client.PostAsJsonAsync("/v1/dossiers", new { analysis_type = "income" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("ACESSO_NEGADO", (await ReadJsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Get_WithOnlyWriteScope_Returns403()
    {
        var writer = factory.CreateClientFor(TenantA, scopes: "parsen/dossiers.write");
        var created = await ReadJsonAsync(await writer.PostAsJsonAsync("/v1/dossiers", new { analysis_type = "income" }));

        var response = await writer.GetAsync($"/v1/dossiers/{created.GetProperty("id").GetString()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_WithOnlyReadScope_Returns200()
    {
        var created = await ReadJsonAsync(
            await factory.CreateClientFor(TenantA).PostAsJsonAsync("/v1/dossiers", new { analysis_type = "income" }));

        var reader = factory.CreateClientFor(TenantA, scopes: "parsen/dossiers.read");
        var response = await reader.GetAsync($"/v1/dossiers/{created.GetProperty("id").GetString()}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
