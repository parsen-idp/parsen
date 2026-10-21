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
using Parsen.Domain.Dossiers;
using Errors = Amazon.CDK.AWS.StepFunctions.Errors;

namespace Parsen.Cdk;

/// <summary>Recursos da stack Data que os workers do pipeline usam.</summary>
internal sealed record PipelineDependencies(ITableV2 Table, IBucket DocumentsBucket, IKey PlatformKey);

/// <summary>
/// Stack do pipeline (spec, seções 4 e 5): barramento de eventos, workers (Lambdas Native AOT) e a
/// máquina de estados que os orquestra. Fase 1: Ingest → limite de páginas → OCR → Extract → needs_review.
/// </summary>
internal sealed class PipelineStack : Stack
{
    /// <summary>Workers só alcançam dados de tenant, nunca itens de plataforma (APICLIENT#).</summary>
    private static readonly string[] TenantKeys = ["TENANT#*"];

    /// <summary>Projetos que todo worker referencia (o build AOT precisa das pastas de todos).</summary>
    private static readonly string[] WorkerDependencies =
        ["src/Parsen.Workers/Common", "src/Parsen.Infrastructure", "src/Parsen.Application", "src/Parsen.Domain", "src/Parsen.Schemas"];

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

        // OCR página a página: até 50 páginas de ~1 s no Textract cabem com folga em 3 minutos
        var ocr = CreateWorker(settings, "Ocr", "ocr", memorySize: 512, timeout: Duration.Minutes(3),
            new Dictionary<string, string>
            {
                ["TABLE_NAME"] = table.TableName,
                ["BUCKET_NAME"] = bucket.BucketName,
                ["TEXTRACT_REGION"] = settings.TextractRegion,
            });
        AllowTenantItems(ocr, table, "dynamodb:GetItem");

        // Lê as páginas e a retenção; grava o textract.json com a mesma tag
        ocr.AddToRolePolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = ["s3:GetObject", "s3:GetObjectTagging", "s3:PutObject", "s3:PutObjectTagging"],
            Resources = [bucket.ArnForObjects("*")],
        }));
        ocr.AddToRolePolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = ["kms:Decrypt", "kms:GenerateDataKey"],
            Resources = [key.KeyArn],
        }));

        // O Textract não tem permissão por recurso: a ação só pode ser liberada com "*"
        ocr.AddToRolePolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = ["textract:DetectDocumentText"],
            Resources = ["*"],
        }));

        // Extração: uma chamada ao Claude por documento (~6 s para um holerite); 2 minutos cobrem documentos longos.
        // 256 MB: usa ~50 MB e passa o tempo esperando o Bedrock, então mais memória (mais CPU) não acelera e custa mais
        var extract = CreateWorker(settings, "Extract", "extract", memorySize: 256, timeout: Duration.Minutes(2),
            new Dictionary<string, string>
            {
                ["TABLE_NAME"] = table.TableName,
                ["BUCKET_NAME"] = bucket.BucketName,
                ["MODEL_ID"] = settings.ExtractionModelId,
            });

        // Lê o documento; grava campos e detalhe, e consulta/apaga campos antigos ao substituir a extração
        AllowTenantItems(extract, table, "dynamodb:GetItem", "dynamodb:Query", "dynamodb:PutItem", "dynamodb:BatchWriteItem");

        // Só lê o textract.json: não grava nada no S3
        extract.AddToRolePolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = ["s3:GetObject"],
            Resources = [bucket.ArnForObjects("*")],
        }));
        extract.AddToRolePolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = ["kms:Decrypt"],
            Resources = [key.KeyArn],
        }));

        // Só o modelo configurado. O profile Global roteia para a região com capacidade: o IAM precisa do
        // profile, do modelo base em qualquer região e do ARN do modelo sem região, usado no roteamento global
        extract.AddToRolePolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = ["bedrock:InvokeModel"],
            Resources =
            [
                $"arn:aws:bedrock:{Region}:{Account}:inference-profile/{settings.ExtractionModelId}",
                $"arn:aws:bedrock:*::foundation-model/{settings.ExtractionFoundationModelId}",
                $"arn:aws:bedrock:::foundation-model/{settings.ExtractionFoundationModelId}",
            ],
        }));

        // ---------- Máquina de estados ----------

        StateMachine = new StateMachine(this, "StateMachine", new StateMachineProps
        {
            StateMachineName = $"parsen-{settings.Name}-pipeline",
            StateMachineType = StateMachineType.STANDARD,
            DefinitionBody = DefinitionBody.FromChainable(BuildDefinition(ingest, ocr, extract, finalize)),

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
    /// Entrada: { tenant_id, dossier_id, document_ids, submitted_at }. Duas fases, para não pagar OCR e modelo
    /// por um dossiê que passa do limite de páginas:
    /// 1. Ingest de todos os documentos (em paralelo) → soma das páginas ≤ limite do dossiê;
    /// 2. OCR → Extract por documento (em paralelo) → dossiê em needs_review.
    /// Qualquer erro definitivo leva o dossiê para failed.
    /// </summary>
    private Chain BuildDefinition(IFunction ingest, IFunction ocr, IFunction extract, IFunction finalize)
    {
        // ---------- Fase 1: Ingest ----------

        var ingestTask = new LambdaInvoke(this, "Ingest", new LambdaInvokeProps
        {
            LambdaFunction = ingest,
            PayloadResponseOnly = true,
            RetryOnServiceExceptions = false,

            // Guarda o resultado (page_count) ao lado dos IDs do item, sem substituí-los
            ResultPath = "$.ingest",
        });
        AddRetries(ingestTask);

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

            // Lista com { tenant_id, dossier_id, document_id, ingest: { page_count } } de cada documento
            ResultPath = "$.ingested",
        });
        ingestDocuments.ItemProcessor(ingestTask);

        // Soma das páginas do dossiê. O Choice usa JSONata ($sum), que a sintaxe JSONPath clássica não tem;
        // o limite vem do domínio (UploadLimits), o mesmo que o Ingest aplica a cada documento
        var checkPageLimit = Choice.Jsonata(this, "CheckPageLimit", new ChoiceJsonataProps
        {
            Comment = $"Recusa o dossiê com mais de {UploadLimits.MaxPagesPerDossier} páginas antes do OCR e do modelo",
        });

        var pageLimitExceeded = new Pass(this, "PageLimitExceeded", new PassProps
        {
            // Mesmo formato do Catch ({ Error, Cause }), para o MarkFailed tratar igual a uma recusa de etapa
            Result = Result.FromObject(new Dictionary<string, object>
            {
                ["Error"] = "PAGINAS_EXCEDIDAS_DOSSIE",
                ["Cause"] = $"O dossiê passa do limite de {UploadLimits.MaxPagesPerDossier} páginas.",
            }),
            ResultPath = "$.error",
        });

        // ---------- Fase 2: OCR → Extract ----------

        var ocrTask = new LambdaInvoke(this, "Ocr", new LambdaInvokeProps
        {
            LambdaFunction = ocr,
            PayloadResponseOnly = true,
            RetryOnServiceExceptions = false,
            Payload = TaskInput.FromObject(new Dictionary<string, object>
            {
                ["tenant_id"] = JsonPath.StringAt("$.tenant_id"),
                ["dossier_id"] = JsonPath.StringAt("$.dossier_id"),
                ["document_id"] = JsonPath.StringAt("$.document_id"),
                ["page_count"] = JsonPath.NumberAt("$.page_count"),
            }),
            ResultPath = "$.ocr",
        });
        AddRetries(ocrTask);

        var extractTask = new LambdaInvoke(this, "Extract", new LambdaInvokeProps
        {
            LambdaFunction = extract,
            PayloadResponseOnly = true,
            RetryOnServiceExceptions = false,
            Payload = TaskInput.FromObject(new Dictionary<string, object>
            {
                ["tenant_id"] = JsonPath.StringAt("$.tenant_id"),
                ["dossier_id"] = JsonPath.StringAt("$.dossier_id"),
                ["document_id"] = JsonPath.StringAt("$.document_id"),
            }),
            ResultPath = "$.extract",
        });
        AddRetries(extractTask);

        // Cada documento passa pelas etapas em sequência; até 5 documentos em paralelo
        var extractDocuments = new Map(this, "ExtractDocuments", new MapProps
        {
            ItemsPath = JsonPath.StringAt("$.ingested"),
            ItemSelector = new Dictionary<string, object>
            {
                ["tenant_id"] = JsonPath.StringAt("$.tenant_id"),
                ["dossier_id"] = JsonPath.StringAt("$.dossier_id"),
                ["document_id"] = JsonPath.StringAt("$$.Map.Item.Value.document_id"),
                ["page_count"] = JsonPath.NumberAt("$$.Map.Item.Value.ingest.page_count"),
            },
            MaxConcurrency = 5,
            ResultPath = "$.documents",
        });
        extractDocuments.ItemProcessor(ocrTask.Next(extractTask));

        // ---------- Fim ----------

        // Todos os documentos extraídos: dossiê em needs_review (Fase 1 não tem verificações)
        var markNeedsReview = new LambdaInvoke(this, "MarkNeedsReview", new LambdaInvokeProps
        {
            LambdaFunction = finalize,
            PayloadResponseOnly = true,
            RetryOnServiceExceptions = false,
            Payload = TaskInput.FromObject(new Dictionary<string, object>
            {
                ["tenant_id"] = JsonPath.StringAt("$.tenant_id"),
                ["dossier_id"] = JsonPath.StringAt("$.dossier_id"),
                ["outcome"] = "needs_review",
            }),
            ResultPath = JsonPath.DISCARD,
        });
        markNeedsReview.AddRetry(TechnicalRetry());

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
        markFailed.Next(new Fail(this, "PipelineFailed", new FailProps
        {
            Error = "PipelineFailed",
            Cause = "Uma etapa falhou de forma definitiva; o dossiê foi marcado como failed.",
        }));

        var catchToFailed = new CatchProps { Errors = [Errors.ALL], ResultPath = "$.error" };
        ingestDocuments.AddCatch(markFailed, catchToFailed);
        extractDocuments.AddCatch(markFailed, catchToFailed);
        markNeedsReview.AddCatch(markFailed, catchToFailed);
        pageLimitExceeded.Next(markFailed);

        checkPageLimit
            .When(
                Condition.Jsonata($"{{% $sum($states.input.ingested.ingest.page_count) > {UploadLimits.MaxPagesPerDossier} %}}"),
                pageLimitExceeded)
            .Otherwise(extractDocuments.Next(markNeedsReview).Next(new Succeed(this, "DossierReadyForReview")));

        return ingestDocuments.Next(checkPageLimit);
    }

    /// <summary>
    /// Recusa de regra (DocumentRejectedException: PDF com senha, imagem inválida...) não melhora com nova
    /// tentativa. Demais falhas: até 3 novas tentativas, esperando 2 s, 4 s e 8 s (regra do CLAUDE.md).
    /// </summary>
    private static void AddRetries(LambdaInvoke task)
    {
        task.AddRetry(new RetryProps { Errors = ["DocumentRejectedException"], MaxAttempts = 0 });
        task.AddRetry(TechnicalRetry());
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
