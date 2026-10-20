using Amazon.CDK;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.Events;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.KMS;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.StepFunctions;
using Amazon.CDK.AWS.StepFunctions.Tasks;
using Constructs;
using Errors = Amazon.CDK.AWS.StepFunctions.Errors;

namespace Parsen.Cdk;

/// <summary>Recursos da stack Data que os workers do pipeline usam.</summary>
internal sealed record PipelineDependencies(ITableV2 Table, IBucket DocumentsBucket, IKey PlatformKey);

/// <summary>
/// Stack do pipeline (spec, seções 4 e 5): barramento de eventos, workers (Lambdas Native AOT) e a
/// máquina de estados que os orquestra. Fase 1: Ingest por documento; OCR, Extract e Result entram nas partes 6c a 6e.
/// </summary>
internal sealed class PipelineStack : Stack
{
    /// <summary>Workers só alcançam dados de tenant, nunca itens de plataforma (APICLIENT#).</summary>
    private static readonly string[] TenantKeys = ["TENANT#*"];

    /// <summary>Projetos que todo worker referencia (o build AOT precisa das pastas de todos).</summary>
    private static readonly string[] WorkerDependencies =
        ["src/Parsen.Workers/Common", "src/Parsen.Infrastructure", "src/Parsen.Application", "src/Parsen.Domain"];

    public PipelineStack(Construct scope, string id, IStackProps props, StageSettings settings, PipelineDependencies dependencies)
        : base(scope, id, props)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        var (table, bucket, key) = dependencies;

        EventBus = new EventBus(this, "EventBus", new EventBusProps
        {
            EventBusName = $"parsen-{settings.Name}",
            Description = "Eventos de integração da Parsen (ex.: DossierSubmitted)",
        });

        AddEventLog(settings);

        // ---------- Workers ----------

        var ingest = CreateWorker(settings, "Ingest", "ingest", memorySize: 1024, timeout: Duration.Seconds(60),
            new Dictionary<string, string> { ["TABLE_NAME"] = table.TableName, ["BUCKET_NAME"] = bucket.BucketName });
        AllowTenantItems(ingest, table, "dynamodb:GetItem");

        // Lê o original e a tag de retenção; grava as páginas com a mesma tag
        ingest.AddToRolePolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = ["s3:GetObject", "s3:GetObjectTagging", "s3:PutObject", "s3:PutObjectTagging"],
            Resources = [bucket.ArnForObjects("*")],
        }));

        // Decifra o original e cifra as páginas com a chave da plataforma
        ingest.AddToRolePolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = ["kms:Decrypt", "kms:GenerateDataKey"],
            Resources = [key.KeyArn],
        }));

        var finalize = CreateWorker(settings, "Finalize", "finalize", memorySize: 256, timeout: Duration.Seconds(30),
            new Dictionary<string, string> { ["TABLE_NAME"] = table.TableName });
        AllowTenantItems(finalize, table, "dynamodb:GetItem", "dynamodb:PutItem");

        // ---------- Máquina de estados ----------

        StateMachine = new StateMachine(this, "StateMachine", new StateMachineProps
        {
            StateMachineName = $"parsen-{settings.Name}-pipeline",
            StateMachineType = StateMachineType.STANDARD,
            DefinitionBody = DefinitionBody.FromChainable(BuildDefinition(ingest, finalize)),

            // Teto de segurança: um dossiê de 50 páginas leva bem menos que isso
            Timeout = Duration.Minutes(15),
        });

        // DossierSubmitted no barramento → uma execução por dossiê, com o "detail" do evento como entrada
        _ = new Rule(this, "StartPipeline", new RuleProps
        {
            EventBus = EventBus,
            Description = "Inicia o pipeline quando a API publica DossierSubmitted",
            EventPattern = new EventPattern
            {
                Source = ["parsen.api"],
                DetailType = ["DossierSubmitted"],
            },
            Targets =
            [
                new Amazon.CDK.AWS.Events.Targets.SfnStateMachine(StateMachine, new Amazon.CDK.AWS.Events.Targets.SfnStateMachineProps
                {
                    Input = RuleTargetInput.FromEventPath("$.detail"),
                }),
            ],
        });

        _ = new CfnOutput(this, "EventBusName", new CfnOutputProps { Value = EventBus.EventBusName });
        _ = new CfnOutput(this, "StateMachineArn", new CfnOutputProps { Value = StateMachine.StateMachineArn });
    }

    public EventBus EventBus { get; }

    public StateMachine StateMachine { get; }

    /// <summary>
    /// Entrada: { tenant_id, dossier_id, document_ids, submitted_at }.
    /// Ingest por documento (em paralelo); qualquer erro definitivo leva o dossiê para failed.
    /// </summary>
    private Chain BuildDefinition(IFunction ingest, IFunction finalize)
    {
        var ingestTask = new LambdaInvoke(this, "Ingest", new LambdaInvokeProps
        {
            LambdaFunction = ingest,
            PayloadResponseOnly = true,
            RetryOnServiceExceptions = false,
        });

        // Recusa de regra (PDF com senha, páginas demais...) não melhora com nova tentativa
        ingestTask.AddRetry(new RetryProps { Errors = ["IngestRejectedException"], MaxAttempts = 0 });

        // Demais falhas: até 3 novas tentativas, esperando 2 s, 4 s e 8 s (regra do CLAUDE.md)
        ingestTask.AddRetry(TechnicalRetry());

        // Um Ingest por documento, até 5 em paralelo; cada item recebe tenant, dossiê e o ID do documento
        var ingestDocuments = new Map(this, "IngestDocuments", new MapProps
        {
            ItemsPath = JsonPath.StringAt("$.document_ids"),
            ItemSelector = new Dictionary<string, object>
            {
                ["tenant_id"] = JsonPath.StringAt("$.tenant_id"),
                ["dossier_id"] = JsonPath.StringAt("$.dossier_id"),
                ["document_id"] = JsonPath.StringAt("$$.Map.Item.Value"),
            },
            MaxConcurrency = 5,
            ResultPath = "$.ingest",
        });
        ingestDocuments.ItemProcessor(ingestTask);

        // Erro definitivo: grava failed no dossiê e encerra a execução como falha (vermelha no console)
        var markFailed = new LambdaInvoke(this, "MarkFailed", new LambdaInvokeProps
        {
            LambdaFunction = finalize,
            PayloadResponseOnly = true,
            RetryOnServiceExceptions = false,
            Payload = TaskInput.FromObject(new Dictionary<string, object>
            {
                ["tenant_id"] = JsonPath.StringAt("$.tenant_id"),
                ["dossier_id"] = JsonPath.StringAt("$.dossier_id"),
                ["outcome"] = "failed",
                // StringAt só marca a chave como caminho ("error.$": "$.error"): o objeto de erro é copiado
                // inteiro. (ObjectAt dentro de um dicionário não passa pela ponte .NET → TypeScript do CDK)
                ["error"] = JsonPath.StringAt("$.error"),
            }),
            ResultPath = JsonPath.DISCARD,
        });
        markFailed.AddRetry(TechnicalRetry());

        ingestDocuments.AddCatch(
            markFailed.Next(new Fail(this, "PipelineFailed", new FailProps
            {
                Error = "PipelineFailed",
                Cause = "Uma etapa falhou de forma definitiva; o dossiê foi marcado como failed.",
            })),
            new CatchProps { Errors = [Errors.ALL], ResultPath = "$.error" });

        // Fase 1, parte 6a: termina após o Ingest (o dossiê segue em processing até as partes 6c a 6e)
        return ingestDocuments.Next(new Succeed(this, "IngestCompleted"));
    }

    private static RetryProps TechnicalRetry() => new()
    {
        Errors = [Errors.ALL],
        Interval = Duration.Seconds(2),
        BackoffRate = 2,
        MaxAttempts = 3,
    };

    private Function CreateWorker(
        StageSettings settings, string id, string name, int memorySize, Duration timeout, Dictionary<string, string> environment)
    {
        var functionName = $"parsen-{settings.Name}-{name}";

        var logGroup = new LogGroup(this, $"{id}Logs", new LogGroupProps
        {
            LogGroupName = $"/aws/lambda/{functionName}",
            Retention = RetentionDays.ONE_MONTH,
            RemovalPolicy = RemovalPolicy.DESTROY,
        });

        return new Function(this, $"{id}Function", new FunctionProps
        {
            FunctionName = functionName,
            Description = $"Pipeline da Parsen: etapa {id}",
            Runtime = Runtime.PROVIDED_AL2023,
            Architecture = Architecture.X86_64,
            Handler = "bootstrap",
            Code = LambdaCode.NativeAot($"src/Parsen.Workers/{id}", WorkerDependencies),
            MemorySize = memorySize,
            Timeout = timeout,
            Environment = environment,
            LogGroup = logGroup,
        });
    }

    private static void AllowTenantItems(Function function, ITableV2 table, params string[] actions) =>
        function.AddToRolePolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = actions,
            Resources = [table.TableArn],
            Conditions = new Dictionary<string, object>
            {
                ["ForAllValues:StringLike"] = new Dictionary<string, object>
                {
                    ["dynamodb:LeadingKeys"] = TenantKeys,
                },
            },
        }));

    /// <summary>
    /// Observação: copia todo evento do barramento para o CloudWatch Logs. Os eventos só levam IDs
    /// (regra 3), então o log não tem dados pessoais.
    /// </summary>
    private void AddEventLog(StageSettings settings)
    {
        var eventLog = new LogGroup(this, "EventLog", new LogGroupProps
        {
            LogGroupName = $"/aws/events/parsen-{settings.Name}",
            Retention = RetentionDays.ONE_MONTH,
            RemovalPolicy = RemovalPolicy.DESTROY,
        });

        _ = new Rule(this, "LogAllEvents", new RuleProps
        {
            EventBus = EventBus,
            Description = "Registra todos os eventos do barramento no CloudWatch Logs",

            // Padrão que casa com qualquer evento da conta: o filtro mínimo exigido pelo EventBridge
            EventPattern = new EventPattern { Account = [Account] },
            Targets = [new Amazon.CDK.AWS.Events.Targets.CloudWatchLogGroup(eventLog)],
        });
    }
}
