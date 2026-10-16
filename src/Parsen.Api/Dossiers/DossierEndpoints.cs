using System.Security.Claims;
using Parsen.Api.Auth;
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
