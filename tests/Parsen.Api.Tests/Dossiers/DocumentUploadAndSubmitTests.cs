using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Parsen.Api.Tests.Infrastructure;

namespace Parsen.Api.Tests.Dossiers;

public sealed class DocumentUploadAndSubmitTests(ParsenApiFactory factory) : IClassFixture<ParsenApiFactory>
{
    private const string TenantA = "ten_a";
    private const string TenantB = "ten_b";
    private const long PdfSize = 53_979;

    // ---------- Pedido de upload ----------

    [Fact]
    public async Task RequestUpload_ValidPdf_Returns201WithUploadTicket()
    {
        var client = factory.CreateClientFor(TenantA);
        var dossierId = await CreateDossierAsync(client);

        var response = await RequestUploadAsync(client, dossierId, "application/pdf", PdfSize);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.StartsWith("doc_", body.GetProperty("document_id").GetString(), StringComparison.Ordinal);
        Assert.Equal(dossierId, body.GetProperty("dossier_id").GetString());
        Assert.Equal("application/pdf", body.GetProperty("content_type").GetString());
        Assert.Equal(PdfSize, body.GetProperty("size_bytes").GetInt64());

        var upload = body.GetProperty("upload");
        Assert.Equal("PUT", upload.GetProperty("method").GetString());
        Assert.StartsWith("https://", upload.GetProperty("url").GetString(), StringComparison.Ordinal);
        Assert.Equal("application/pdf", upload.GetProperty("headers").GetProperty("Content-Type").GetString());
        Assert.Equal("2026-10-15T12:15:00Z", upload.GetProperty("expires_at").GetString());
    }

    [Theory]
    [InlineData("image/heic", PdfSize, "FORMATO_NAO_SUPORTADO")]
    [InlineData("application/pdf", 20L * 1024 * 1024 + 1, "TAMANHO_EXCEDIDO")]
    [InlineData("application/pdf", 0, "REQUISICAO_INVALIDA")]
    public async Task RequestUpload_InvalidFile_Returns400(string contentType, long size, string expectedCode)
    {
        var client = factory.CreateClientFor(TenantA);
        var dossierId = await CreateDossierAsync(client);

        var response = await RequestUploadAsync(client, dossierId, contentType, size);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedCode, (await ReadJsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task RequestUpload_OtherTenantsDossier_Returns404()
    {
        var dossierId = await CreateDossierAsync(factory.CreateClientFor(TenantA));

        var response = await RequestUploadAsync(factory.CreateClientFor(TenantB), dossierId, "application/pdf", PdfSize);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("DOSSIE_NAO_ENCONTRADO", (await ReadJsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task RequestUpload_AfterSubmit_Returns409()
    {
        var client = factory.CreateClientFor(TenantA);
        var dossierId = await CreateSubmittedDossierAsync(client);

        var response = await RequestUploadAsync(client, dossierId, "application/pdf", PdfSize);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("DOSSIE_NAO_EDITAVEL", (await ReadJsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task RequestUpload_WithOnlyReadScope_Returns403()
    {
        var dossierId = await CreateDossierAsync(factory.CreateClientFor(TenantA));

        var response = await RequestUploadAsync(
            factory.CreateClientFor(TenantA, scopes: "parsen/dossiers.read"), dossierId, "application/pdf", PdfSize);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------- Submit ----------

    [Fact]
    public async Task Submit_WithUploadedFiles_Returns202ProcessingAndPublishesEvent()
    {
        var client = factory.CreateClientFor(TenantA);
        var dossierId = await CreateDossierAsync(client);
        var documentId = await UploadAsync(client, dossierId, PdfSize);

        var response = await client.PostAsync($"/v1/dossiers/{dossierId}/submit", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("processing", (await ReadJsonAsync(response)).GetProperty("status").GetString());

        var published = Assert.Single(factory.Events.Published, e => e.DossierId.ToString() == dossierId);
        Assert.Equal(TenantA, published.TenantId.Value);
        Assert.Equal([documentId], published.DocumentIds.Select(d => d.ToString()));
    }

    [Fact]
    public async Task Submit_WithoutDocuments_Returns400()
    {
        var client = factory.CreateClientFor(TenantA);
        var dossierId = await CreateDossierAsync(client);

        var response = await client.PostAsync($"/v1/dossiers/{dossierId}/submit", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("DOSSIE_SEM_DOCUMENTOS", (await ReadJsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Submit_WithFileNotUploaded_Returns400AndStaysDraft()
    {
        var client = factory.CreateClientFor(TenantA);
        var dossierId = await CreateDossierAsync(client);
        await RequestUploadAsync(client, dossierId, "application/pdf", PdfSize);

        var response = await client.PostAsync($"/v1/dossiers/{dossierId}/submit", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ARQUIVO_NAO_ENVIADO", (await ReadJsonAsync(response)).GetProperty("code").GetString());
        Assert.Equal("draft", await GetStatusAsync(client, dossierId));
    }

    [Fact]
    public async Task Submit_WithUploadedFileTooLarge_Returns400()
    {
        var client = factory.CreateClientFor(TenantA);
        var dossierId = await CreateDossierAsync(client);
        await UploadAsync(client, dossierId, actualSize: 20L * 1024 * 1024 + 1);

        var response = await client.PostAsync($"/v1/dossiers/{dossierId}/submit", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("TAMANHO_EXCEDIDO", (await ReadJsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Submit_Twice_Returns409()
    {
        var client = factory.CreateClientFor(TenantA);
        var dossierId = await CreateSubmittedDossierAsync(client);

        var response = await client.PostAsync($"/v1/dossiers/{dossierId}/submit", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Submit_WhenEventPublishingFails_Returns503AndRevertsToDraft()
    {
        var client = factory.CreateClientFor(TenantA);
        var dossierId = await CreateDossierAsync(client);
        await UploadAsync(client, dossierId, PdfSize);
        factory.Events.FailFor = dossierId;

        var response = await client.PostAsync($"/v1/dossiers/{dossierId}/submit", null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("TENTE_NOVAMENTE", (await ReadJsonAsync(response)).GetProperty("code").GetString());
        Assert.Equal("draft", await GetStatusAsync(client, dossierId));

        // Com o EventBridge de volta, o cliente consegue submeter de novo
        factory.Events.FailFor = null;
        var retry = await client.PostAsync($"/v1/dossiers/{dossierId}/submit", null);
        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
    }

    [Fact]
    public async Task Submit_OtherTenantsDossier_Returns404()
    {
        var dossierId = await CreateDossierAsync(factory.CreateClientFor(TenantA));

        var response = await factory.CreateClientFor(TenantB).PostAsync($"/v1/dossiers/{dossierId}/submit", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- Auxiliares ----------

    private static async Task<string> CreateDossierAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/v1/dossiers", new { analysis_type = "income" });
        return (await ReadJsonAsync(response)).GetProperty("id").GetString()!;
    }

    private static Task<HttpResponseMessage> RequestUploadAsync(HttpClient client, string dossierId, string contentType, long size) =>
        client.PostAsJsonAsync($"/v1/dossiers/{dossierId}/documents", new { content_type = contentType, size_bytes = size });

    /// <summary>Pede a URL e simula o cliente enviando o arquivo ao S3.</summary>
    private async Task<string> UploadAsync(HttpClient client, string dossierId, long actualSize)
    {
        var declared = Math.Min(actualSize, 20L * 1024 * 1024);
        var ticket = await ReadJsonAsync(await RequestUploadAsync(client, dossierId, "application/pdf", declared));
        var documentId = ticket.GetProperty("document_id").GetString()!;
        factory.Files.SimulateUpload(documentId, actualSize);
        return documentId;
    }

    private async Task<string> CreateSubmittedDossierAsync(HttpClient client)
    {
        var dossierId = await CreateDossierAsync(client);
        await UploadAsync(client, dossierId, PdfSize);
        var response = await client.PostAsync($"/v1/dossiers/{dossierId}/submit", null);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return dossierId;
    }

    private static async Task<string?> GetStatusAsync(HttpClient client, string dossierId) =>
        (await ReadJsonAsync(await client.GetAsync($"/v1/dossiers/{dossierId}"))).GetProperty("status").GetString();

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
