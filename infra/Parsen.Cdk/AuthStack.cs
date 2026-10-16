using Amazon.CDK;
using Amazon.CDK.AWS.Cognito;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.Logs;
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

    /// <summary>Prefixo dos itens de plataforma que o gatilho pode ler (spec, seção 9).</summary>
    private static readonly string[] PlatformClientKeys = ["APICLIENT#*"];

    public AuthStack(Construct scope, string id, IStackProps props, StageSettings settings, ITableV2 table)
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

        // Gatilho V3: adiciona tenant_id aos tokens de usuários e de integrações (client credentials)
        var preTokenGeneration = CreatePreTokenGenerationFunction(settings, table);
        UserPool.AddTrigger(UserPoolOperation.PRE_TOKEN_GENERATION_CONFIG, preTokenGeneration, LambdaVersion.V3_0);

        // Endereço do endpoint de tokens: https://{prefixo}.auth.{região}.amazoncognito.com/oauth2/token
        var domain = UserPool.AddDomain("Domain", new UserPoolDomainOptions
        {
            CognitoDomain = new CognitoDomainOptions { DomainPrefix = $"parsen-{settings.Name}-{Account}" },
        });

        _ = new CfnOutput(this, "UserPoolId", new CfnOutputProps { Value = UserPool.UserPoolId });
        Issuer = $"https://cognito-idp.{Region}.amazonaws.com/{UserPool.UserPoolId}";
        _ = new CfnOutput(this, "Issuer", new CfnOutputProps { Value = Issuer });
        _ = new CfnOutput(this, "TokenEndpoint", new CfnOutputProps { Value = $"{domain.BaseUrl()}/oauth2/token" });
    }

    public UserPool UserPool { get; }

    /// <summary>Emissor dos tokens; a API valida a assinatura pelo JWKS em {Issuer}/.well-known/jwks.json.</summary>
    public string Issuer { get; }

    public UserPoolResourceServer ResourceServer { get; }

    private Function CreatePreTokenGenerationFunction(StageSettings settings, ITableV2 table)
    {
        var functionName = $"parsen-{settings.Name}-pre-token-generation";

        // Sem retenção definida, os logs ficariam para sempre (e custariam). Logs sem dados pessoais (regra 3)
        var logGroup = new LogGroup(this, "PreTokenGenerationLogs", new LogGroupProps
        {
            LogGroupName = $"/aws/lambda/{functionName}",
            Retention = RetentionDays.ONE_MONTH,
            RemovalPolicy = RemovalPolicy.DESTROY,
        });

        var function = new Function(this, "PreTokenGeneration", new FunctionProps
        {
            FunctionName = functionName,
            Description = "Adiciona tenant_id aos tokens do Cognito (gatilho Pre Token Generation V3)",

            // Native AOT: o runtime provided.al2023 executa o arquivo "bootstrap"
            Runtime = Runtime.PROVIDED_AL2023,
            Architecture = Architecture.X86_64,
            Handler = "bootstrap",
            Code = LambdaCode.NativeAot("src/Parsen.Auth.PreTokenGeneration"),

            MemorySize = 256,

            // O Cognito espera no máximo 5 segundos pela resposta do gatilho
            Timeout = Duration.Seconds(5),

            Environment = new Dictionary<string, string> { ["TABLE_NAME"] = table.TableName },
            LogGroup = logGroup,
        });

        // Permissão mínima: ler só itens de plataforma APICLIENT#*, nunca dados de tenant (TENANT#...)
        function.AddToRolePolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = ["dynamodb:GetItem"],
            Resources = [table.TableArn],
            Conditions = new Dictionary<string, object>
            {
                ["ForAllValues:StringLike"] = new Dictionary<string, object>
                {
                    ["dynamodb:LeadingKeys"] = PlatformClientKeys,
                },
            },
        }));

        return function;
    }
}
