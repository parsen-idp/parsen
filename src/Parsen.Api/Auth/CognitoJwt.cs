using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Parsen.Api.Auth;

/// <summary>Validação dos tokens JWT emitidos pelo Cognito (ou pelo dotnet user-jwts no desenvolvimento).</summary>
internal static class CognitoJwt
{
    public static void Configure(JwtBearerOptions options, string? cognitoIssuer)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Mantém os nomes originais dos claims (scope, tenant_id, sub), sem o mapeamento para URIs da Microsoft
        options.MapInboundClaims = false;

        if (string.IsNullOrWhiteSpace(cognitoIssuer))
        {
            // Desenvolvimento: emissor e chave vêm da configuração gerada pelo dotnet user-jwts
            return;
        }

        // A chave pública vem do JWKS do emissor ({issuer}/.well-known/jwks.json), baixado e guardado em cache
        options.Authority = cognitoIssuer;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = cognitoIssuer,

            // Access tokens do Cognito não têm "aud" (trazem client_id); o acesso é controlado pelos scopes
            ValidateAudience = false,
        };

        options.Events = new JwtBearerEvents
        {
            // Só access tokens acessam a API; o ID token do Cognito serve para o app saber quem é o usuário
            OnTokenValidated = context =>
            {
                if (context.Principal?.FindFirst("token_use")?.Value != "access")
                {
                    context.Fail("Somente access tokens são aceitos.");
                }

                return Task.CompletedTask;
            },
        };
    }
}
