using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Parsen.Api.Auth;

/// <summary>
/// Scopes do resource server "parsen" no Cognito. Cada operação exige o seu; sem ele, a API responde 403.
/// </summary>
internal static class Scopes
{
    public const string DossiersRead = "parsen/dossiers.read";
    public const string DossiersWrite = "parsen/dossiers.write";

    public static void AddPolicies(AuthorizationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        foreach (var scope in new[] { DossiersRead, DossiersWrite })
        {
            builder.AddPolicy(scope, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => Has(context.User, scope)));
        }
    }

    /// <summary>
    /// O Cognito envia um único claim "scope" separado por espaços; o dotnet user-jwts envia um claim por scope.
    /// </summary>
    public static bool Has(ClaimsPrincipal user, string scope) =>
        user.FindAll("scope")
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(scope, StringComparer.Ordinal);
}
