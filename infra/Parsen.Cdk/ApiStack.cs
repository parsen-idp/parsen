using Amazon.CDK;
using Amazon.CDK.AWS.APIGateway;
using Amazon.CDK.AWS.Cognito;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.Logs;
using Constructs;

namespace Parsen.Cdk;

/// <summary>
/// Stack da API pública: Lambda com a Minimal API (runtime dotnet10) atrás do API Gateway REST,
/// em modo proxy, com autorizador do Cognito e limite de taxa global (spec, seções 4 e 7).
/// </summary>
internal sealed class ApiStack : Stack
{
    /// <summary>Scopes aceitos pelo autorizador. O scope de cada operação é conferido na API.</summary>
    private static readonly string[] ApiScopes = ["parsen/dossiers.read", "parsen/dossiers.write"];

    /// <summary>A API só alcança dados de tenant, nunca itens de plataforma (APICLIENT#).</summary>
    private static readonly string[] TenantKeys = ["TENANT#*"];

    public ApiStack(Construct scope, string id, IStackProps props, StageSettings settings, ITableV2 table, IUserPool userPool, string issuer)
        : base(scope, id, props)
    {
        var function = CreateFunction(settings, table, issuer);

        var authorizer = new CognitoUserPoolsAuthorizer(this, "Authorizer", new CognitoUserPoolsAuthorizerProps
        {
            AuthorizerName = "cognito",
            CognitoUserPools = [userPool],
        });

        var api = new LambdaRestApi(this, "RestApi", new LambdaRestApiProps
        {
            RestApiName = $"parsen-{settings.Name}",
            Description = $"Parsen ({settings.Name}): API pública",
            Handler = function,

            // Proxy: todo caminho e método vai para a Lambda; o roteamento fica no ASP.NET
            Proxy = true,

            // Regional: atende direto de sa-east-1 (o tipo EDGE colocaria um CloudFront na frente)
            EndpointConfiguration = new EndpointConfiguration { Types = [EndpointType.REGIONAL] },

            // Token inválido, expirado ou sem scope da Parsen é recusado aqui, sem chamar a Lambda.
            // Sem scopes declarados, o autorizador esperaria um ID token e recusaria tokens de máquina
            DefaultMethodOptions = new MethodOptions
            {
                AuthorizationType = AuthorizationType.COGNITO,
                Authorizer = authorizer,
                AuthorizationScopes = ApiScopes,
            },

            DeployOptions = new StageOptions
            {
                StageName = settings.Name,

                // Limite global (requisições/s e pico). Limite por tenant: usage plan antes do piloto
                ThrottlingRateLimit = 50,
                ThrottlingBurstLimit = 100,
            },

            // A role de logs do API Gateway é configuração da conta inteira; fica para quando ativarmos logs de acesso
            CloudWatchRole = false,
        });

        AddProblemDetailsResponses(api);

        _ = new CfnOutput(this, "ApiUrl", new CfnOutputProps { Value = api.Url });
    }

    private Function CreateFunction(StageSettings settings, ITableV2 table, string issuer)
    {
        var functionName = $"parsen-{settings.Name}-api";

        var logGroup = new LogGroup(this, "ApiLogs", new LogGroupProps
        {
            LogGroupName = $"/aws/lambda/{functionName}",
            Retention = RetentionDays.ONE_MONTH,
            RemovalPolicy = RemovalPolicy.DESTROY,
        });

        var function = new Function(this, "ApiFunction", new FunctionProps
        {
            FunctionName = functionName,
            Description = "API pública da Parsen (ASP.NET Core Minimal API)",

            // Runtime gerenciado do .NET 10; o handler de um executável ASP.NET é o nome do assembly
            Runtime = Runtime.DOTNET_10,
            Architecture = Architecture.X86_64,
            Handler = "Parsen.Api",
            Code = LambdaCode.Managed("src/Parsen.Api", "src/Parsen.Infrastructure", "src/Parsen.Application", "src/Parsen.Domain"),

            // Na Lambda, mais memória também traz mais CPU: encurta o cold start do ASP.NET
            MemorySize = 512,

            // O API Gateway encerra a requisição em 29 s de qualquer forma
            Timeout = Duration.Seconds(29),

            Environment = new Dictionary<string, string>
            {
                ["Auth__CognitoIssuer"] = issuer,
                ["Persistence__Provider"] = "DynamoDb",
                ["Persistence__TableName"] = table.TableName,
            },
            LogGroup = logGroup,
        });

        // Permissão mínima: só as operações usadas e só chaves de tenant (tabela e índices)
        function.AddToRolePolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = ["dynamodb:GetItem", "dynamodb:PutItem", "dynamodb:UpdateItem", "dynamodb:Query"],
            Resources = [table.TableArn, $"{table.TableArn}/index/*"],
            Conditions = new Dictionary<string, object>
            {
                ["ForAllValues:StringLike"] = new Dictionary<string, object>
                {
                    ["dynamodb:LeadingKeys"] = TenantKeys,
                },
            },
        }));

        return function;
    }

    /// <summary>
    /// Erros gerados pelo próprio API Gateway (antes da Lambda) também seguem o contrato da API:
    /// Problem Details com "code", em vez do {"message": "..."} padrão da AWS.
    /// </summary>
    private static void AddProblemDetailsResponses(RestApi api)
    {
        foreach (var (id, type, status, title, code) in new[]
        {
            ("Unauthorized", ResponseType_.UNAUTHORIZED, "401", "Unauthorized", "NAO_AUTENTICADO"),
            ("AccessDenied", ResponseType_.ACCESS_DENIED, "403", "Forbidden", "ACESSO_NEGADO"),
            ("Throttled", ResponseType_.THROTTLED, "429", "Too Many Requests", "LIMITE_EXCEDIDO"),
            ("Default4xx", ResponseType_.DEFAULT_4XX, null, "Bad Request", "REQUISICAO_INVALIDA"),
            ("Default5xx", ResponseType_.DEFAULT_5XX, null, "Internal Server Error", "ERRO_INTERNO"),
        })
        {
            var statusField = status is null ? "$context.error.statusCode" : status;
            api.AddGatewayResponse(id, new GatewayResponseOptions
            {
                Type = type,
                StatusCode = status,
                ResponseHeaders = new Dictionary<string, string> { ["Content-Type"] = "'application/problem+json'" },
                Templates = new Dictionary<string, string>
                {
                    ["application/json"] =
                        $$"""{"type":"about:blank","title":"{{title}}","status":{{statusField}},"code":"{{code}}","trace_id":"$context.requestId"}""",
                },
            });
        }
    }
}
