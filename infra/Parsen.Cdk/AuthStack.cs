using Amazon.CDK;
using Amazon.CDK.AWS.Cognito;
using Constructs;

namespace Parsen.Cdk;

/// <summary>
/// Stack de autenticação: user pool do Cognito para usuários do backoffice (com MFA) e para
/// integrações máquina a máquina (client credentials). Os app clients das fintechs são criados
/// dinamicamente por tenant, não pelo CDK.
/// </summary>
internal sealed class AuthStack : Stack
{
    /// <summary>Identificador do resource server: os scopes ficam como "parsen/dossiers.read".</summary>
    public const string ResourceServerId = "parsen";

    public AuthStack(Construct scope, string id, IStackProps props, StageSettings settings)
        : base(scope, id, props)
    {
        UserPool = new UserPool(this, "UserPool", new UserPoolProps
        {
            UserPoolName = $"parsen-{settings.Name}",

            // Essentials: necessário para o gatilho de token de máquina (V3); grátis até 10 mil usuários/mês
            FeaturePlan = FeaturePlan.ESSENTIALS,

            // Usuários são convidados por um admin do tenant; ninguém se cadastra sozinho
            SelfSignUpEnabled = false,
            SignInAliases = new SignInAliases { Email = true },
            SignInCaseSensitive = false,
            StandardAttributes = new StandardAttributes
            {
                Email = new StandardAttribute { Required = true, Mutable = true },
            },

            // Tenant do usuário: imutável depois do cadastro
            CustomAttributes = new Dictionary<string, ICustomAttribute>
            {
                ["tenant_id"] = new StringAttribute(new StringAttributeProps { MinLen = 1, MaxLen = 64, Mutable = false }),
            },

            // MFA obrigatório no backoffice (spec, seção 11), por aplicativo autenticador (sem custo de SMS)
            Mfa = Mfa.REQUIRED,
            MfaSecondFactor = new MfaSecondFactor { Otp = true, Sms = false },
            PasswordPolicy = new PasswordPolicy
            {
                MinLength = 12,
                RequireLowercase = true,
                RequireUppercase = true,
                RequireDigits = true,
                RequireSymbols = true,
            },
            AccountRecovery = AccountRecovery.EMAIL_ONLY,

            DeletionProtection = settings.IsProduction,
            RemovalPolicy = RemovalPolicy.RETAIN,
        });

        // Papéis do backoffice (spec, seção 8)
        foreach (var (groupId, name, description) in new[]
        {
            ("AnalystGroup", "analista", "Revisa, corrige, aprova e rejeita dossiês"),
            ("SupervisorGroup", "supervisor", "Acompanha auditoria e indicadores"),
            ("AdminGroup", "admin", "Gerencia usuários, integrações e configurações do tenant"),
        })
        {
            UserPool.AddGroup(groupId, new UserPoolGroupOptions { GroupName = name, Description = description });
        }

        // "A API da Parsen" e o que um token pode fazer nela
        ResourceServer = UserPool.AddResourceServer("ApiResourceServer", new UserPoolResourceServerOptions
        {
            Identifier = ResourceServerId,
            UserPoolResourceServerName = "Parsen API",
            Scopes =
            [
                new ResourceServerScope(new ResourceServerScopeProps { ScopeName = "dossiers.read", ScopeDescription = "Consultar dossiês e documentos" }),
                new ResourceServerScope(new ResourceServerScopeProps { ScopeName = "dossiers.write", ScopeDescription = "Criar dossiês, enviar documentos e submeter" }),
            ],
        });

        // Endereço do endpoint de tokens: https://{prefixo}.auth.{região}.amazoncognito.com/oauth2/token
        var domain = UserPool.AddDomain("Domain", new UserPoolDomainOptions
        {
            CognitoDomain = new CognitoDomainOptions { DomainPrefix = $"parsen-{settings.Name}-{Account}" },
        });

        _ = new CfnOutput(this, "UserPoolId", new CfnOutputProps { Value = UserPool.UserPoolId });
        _ = new CfnOutput(this, "Issuer", new CfnOutputProps { Value = $"https://cognito-idp.{Region}.amazonaws.com/{UserPool.UserPoolId}" });
        _ = new CfnOutput(this, "TokenEndpoint", new CfnOutputProps { Value = $"{domain.BaseUrl()}/oauth2/token" });
    }

    public UserPool UserPool { get; }

    public UserPoolResourceServer ResourceServer { get; }
}
