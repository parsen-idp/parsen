using System.Security.Claims;
using Parsen.Api.Auth;
using Parsen.Application.Documents;
using Parsen.Application.Dossiers;
using Parsen.Domain.Dossiers;

namespace Parsen.Api.Dossiers;

internal static class DossierEndpoints
{
    public static IEndpointRouteBuilder MapDossierEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/dossiers")
            .RequireAuthorization()
            .AddEndpointFilter<TenantFilter>();

        group.MapPost("", CreateAsync).RequireAuthorization(Scopes.DossiersWrite);
        group.MapGet("{id}", GetAsync).RequireAuthorization(Scopes.DossiersRead);
        group.MapPost("{id}/documents", RequestUploadAsync).RequireAuthorization(Scopes.DossiersWrite);
        group.MapPost("{id}/submit", SubmitAsync).RequireAuthorization(Scopes.DossiersWrite);

        return app;
    }

    private static async Task<IResult> CreateAsync(
        CreateDossierRequest request,
        ClaimsPrincipal user,
        CreateDossierHandler handler,
        CancellationToken cancellationToken)
    {
        if (request.AnalysisType is not { } analysisType)
        {
            return ApiProblems.InvalidRequest("O campo analysis_type é obrigatório.");
        }

        var command = new CreateDossierCommand(TenantClaims.GetTenantId(user), analysisType, request.ExternalRef);
        var dossier = await handler.HandleAsync(command, cancellationToken);

        return Results.Created($"/v1/dossiers/{dossier.Id}", DossierResponse.From(dossier));
    }

    private static async Task<IResult> GetAsync(
        string id,
        ClaimsPrincipal user,
        GetDossierHandler handler,
        CancellationToken cancellationToken)
    {
        // ID malformado ou de outro tenant: 404, sem revelar se o ID existe
        if (!DossierId.TryParse(id, out var dossierId))
        {
            return ApiProblems.DossierNotFound();
        }

        var dossier = await handler.HandleAsync(TenantClaims.GetTenantId(user), dossierId, cancellationToken);

        return dossier is null ? ApiProblems.DossierNotFound() : Results.Ok(DossierResponse.From(dossier));
    }

    private static async Task<IResult> RequestUploadAsync(
        string id,
        RequestUploadRequest request,
        ClaimsPrincipal user,
        RequestUploadHandler handler,
        CancellationToken cancellationToken)
    {
        if (!DossierId.TryParse(id, out var dossierId))
        {
            return ApiProblems.DossierNotFound();
        }

        if (request.SizeBytes is not { } sizeBytes)
        {
            return ApiProblems.InvalidRequest("O campo size_bytes é obrigatório.");
        }

        var command = new RequestUploadCommand(TenantClaims.GetTenantId(user), dossierId, request.ContentType, sizeBytes);
        var result = await handler.HandleAsync(command, cancellationToken);

        return Results.Json(DocumentUploadResponse.From(result), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> SubmitAsync(
        string id,
        ClaimsPrincipal user,
        SubmitDossierHandler handler,
        CancellationToken cancellationToken)
    {
        if (!DossierId.TryParse(id, out var dossierId))
        {
            return ApiProblems.DossierNotFound();
        }

        var dossier = await handler.HandleAsync(TenantClaims.GetTenantId(user), dossierId, cancellationToken);

        // 202: aceito; o processamento segue em segundo plano e o resultado chega por webhook
        return Results.Accepted($"/v1/dossiers/{dossier.Id}", DossierResponse.From(dossier));
    }
}

internal sealed record CreateDossierRequest(AnalysisType? AnalysisType, string? ExternalRef);

internal sealed record DossierResponse(
    string Id,
    AnalysisType AnalysisType,
    DossierStatus Status,
    string? ExternalRef,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    // DateTime em UTC serializa com "Z" (2026-10-15T12:00:00Z)
    public static DossierResponse From(Dossier dossier) => new(
        dossier.Id.ToString(),
        dossier.AnalysisType,
        dossier.Status,
        dossier.ExternalRef,
        dossier.CreatedAt.UtcDateTime,
        dossier.UpdatedAt.UtcDateTime);
}

/// <summary>O nome do arquivo não é aceito: pode conter dados pessoais (regra 3).</summary>
internal sealed record RequestUploadRequest(string? ContentType, long? SizeBytes);

internal sealed record DocumentUploadResponse(
    string DocumentId,
    string DossierId,
    string ContentType,
    long SizeBytes,
    UploadResponse Upload)
{
    public static DocumentUploadResponse From(RequestUploadResult result) => new(
        result.Document.Id.ToString(),
        result.Document.DossierId.ToString(),
        UploadLimits.ToMimeType(result.Document.ContentType),
        result.Document.DeclaredSizeBytes,
        new UploadResponse(
            result.Ticket.Url.ToString(),
            result.Ticket.Method,
            result.Ticket.Headers,
            result.Ticket.ExpiresAt.UtcDateTime));
}

/// <summary>Como enviar o arquivo: o cliente faz {method} em {url} com exatamente estes cabeçalhos.</summary>
internal sealed record UploadResponse(string Url, string Method, IReadOnlyDictionary<string, string> Headers, DateTime ExpiresAt);
