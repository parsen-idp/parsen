using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Parsen.Api.Tests.Infrastructure;

/// <summary>
/// Simula o token nos testes: o cabeçalho X-Test-Tenant vira o claim tenant_id.
/// Sem o cabeçalho, a requisição não é autenticada. Com o valor "&lt;none&gt;", autentica sem tenant.
/// X-Test-Scopes define o claim scope (separado por espaço, como no Cognito); ausente, recebe todos os scopes.
/// </summary>
internal sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string TenantHeader = "X-Test-Tenant";
    public const string NoTenant = "<none>";
    public const string ScopesHeader = "X-Test-Scopes";
    public const string AllScopes = "parsen/dossiers.read parsen/dossiers.write";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(TenantHeader, out var tenant))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var scopes = Request.Headers.TryGetValue(ScopesHeader, out var requested) ? requested.ToString() : AllScopes;
        var claims = new List<Claim> { new("sub", "usr_teste"), new("scope", scopes) };
        if (tenant != NoTenant)
        {
            claims.Add(new Claim("tenant_id", tenant.ToString()));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
