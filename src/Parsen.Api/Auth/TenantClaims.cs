using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Parsen.Domain.Tenants;

namespace Parsen.Api.Auth;

/// <summary>O tenant vem sempre do claim tenant_id do token, nunca do corpo ou da query (regra 2).</summary>
internal static class TenantClaims
{
    public const string ClaimType = "tenant_id";

    public static bool TryGetTenantId(ClaimsPrincipal user, [NotNullWhen(true)] out TenantId? tenantId)
    {
        var value = user.FindFirstValue(ClaimType);
        tenantId = string.IsNullOrWhiteSpace(value) ? null : TenantId.From(value);
        return tenantId is not null;
    }

    /// <summary>Uso após o <see cref="TenantFilter"/>, que já garantiu a presença do claim.</summary>
    public static TenantId GetTenantId(ClaimsPrincipal user) =>
        TryGetTenantId(user, out var tenantId)
            ? tenantId.Value
            : throw new InvalidOperationException("Token sem tenant_id: o TenantFilter deveria ter barrado a requisição.");
}

/// <summary>Recusa com 401 um token válido que não traz o claim tenant_id.</summary>
internal sealed class TenantFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        return TenantClaims.TryGetTenantId(context.HttpContext.User, out _)
            ? await next(context)
            : ApiProblems.Create(StatusCodes.Status401Unauthorized, "TENANT_AUSENTE", "O token não identifica o tenant.");
    }
}
