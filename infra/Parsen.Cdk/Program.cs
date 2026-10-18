using Amazon.CDK;
using Parsen.Cdk;

var app = new App();

// Estágio (dev, prod...) vem do contexto: cdk synth -c stage=prod. Padrão: dev
var stage = app.Node.TryGetContext("stage") as string ?? "dev";

// Conta e região vêm do perfil da CLI (--profile parsen); nunca fixas no código
var env = new Amazon.CDK.Environment
{
    Account = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_ACCOUNT"),
    Region = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_REGION"),
};

var settings = new StageSettings(stage);

var data = new DataStack(app, $"Parsen-{settings.Name}-Data", new StackProps
{
    Env = env,
    Description = $"Parsen ({settings.Name}): dados — DynamoDB, S3 e KMS",
}, settings);

// A stack Auth usa a tabela da Data (gatilho do Cognito lê APICLIENT#): o CDK cria o export/import
var auth = new AuthStack(app, $"Parsen-{settings.Name}-Auth", new StackProps
{
    Env = env,
    Description = $"Parsen ({settings.Name}): autenticação — Cognito",
}, settings, data.Table);

// Barramento de eventos (e, no passo 6, o Step Functions): a API publica nele
var pipeline = new PipelineStack(app, $"Parsen-{settings.Name}-Pipeline", new StackProps
{
    Env = env,
    Description = $"Parsen ({settings.Name}): pipeline — EventBridge",
}, settings);

_ = new ApiStack(app, $"Parsen-{settings.Name}-Api", new StackProps
{
    Env = env,
    Description = $"Parsen ({settings.Name}): API pública — API Gateway e Lambda",
}, settings, new ApiDependencies(data.Table, auth.UserPool, auth.Issuer, data.DocumentsBucket, data.PlatformKey, pipeline.EventBus));

// Tags aplicadas a todos os recursos: facilitam custo por projeto/estágio no Billing
Tags.Of(app).Add("Project", "parsen");
Tags.Of(app).Add("Stage", settings.Name);

app.Synth();
