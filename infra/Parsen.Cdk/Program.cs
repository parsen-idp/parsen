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

_ = new DataStack(app, $"Parsen-{settings.Name}-Data", new StackProps
{
    Env = env,
    Description = $"Parsen ({settings.Name}): dados — DynamoDB, S3 e KMS",
}, settings);

// Tags aplicadas a todos os recursos: facilitam custo por projeto/estágio no Billing
Tags.Of(app).Add("Project", "parsen");
Tags.Of(app).Add("Stage", settings.Name);

app.Synth();
