using System.Globalization;
using Amazon.CDK;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.KMS;
using Amazon.CDK.AWS.S3;
using Constructs;
using Attribute = Amazon.CDK.AWS.DynamoDB.Attribute;

namespace Parsen.Cdk;

/// <summary>Stack de dados: tabela DynamoDB, bucket S3 de documentos e chave KMS (spec, seção 9).</summary>
internal sealed class DataStack : Stack
{
    /// <summary>Prazos de retenção dos originais oferecidos no painel (spec, seção 11).</summary>
    private static readonly int[] RetentionDays = [7, 30, 90, 180, 365];

    public DataStack(Construct scope, string id, IStackProps props, StageSettings settings)
        : base(scope, id, props)
    {
        Table = CreateTable(settings);
        PlatformKey = CreatePlatformKey(settings);
        DocumentsBucket = CreateDocumentsBucket(settings, PlatformKey);
    }

    /// <summary>Tabela única da Parsen; toda PK começa com TENANT#{tenantId}.</summary>
    public TableV2 Table { get; }

    /// <summary>
    /// Chave padrão do bucket. As chaves por tenant são criadas pela aplicação no cadastro do tenant,
    /// não pelo CDK (tenants nascem dinamicamente).
    /// </summary>
    public Key PlatformKey { get; }

    /// <summary>Originais, páginas renderizadas e saída do Textract, sob o prefixo {tenantId}/.</summary>
    public Bucket DocumentsBucket { get; }

    private Key CreatePlatformKey(StageSettings settings) => new(this, "PlatformKey", new KeyProps
    {
        Alias = $"alias/parsen-{settings.Name}-platform",
        Description = $"Parsen ({settings.Name}): chave padrão do bucket de documentos",
        EnableKeyRotation = true,

        // Apagar a chave tornaria irrecuperável tudo o que foi cifrado com ela
        RemovalPolicy = RemovalPolicy.RETAIN,
    });

    private Bucket CreateDocumentsBucket(StageSettings settings, Key key)
    {
        // Uma regra por prazo: o objeto recebe a tag retention-days e expira sozinho
        var retentionRules = RetentionDays.Select(days => new LifecycleRule
        {
            Id = $"retention-{days}d",
            Expiration = Duration.Days(days),
            TagFilters = new Dictionary<string, object>
            {
                ["retention-days"] = days.ToString(CultureInfo.InvariantCulture),
            },
        });

        var abortIncompleteUploads = new LifecycleRule
        {
            Id = "abort-incomplete-multipart-uploads",
            AbortIncompleteMultipartUploadAfter = Duration.Days(1),
        };

        return new Bucket(this, "DocumentsBucket", new BucketProps
        {
            // Nome de bucket é único no mundo: o número da conta evita colisão
            BucketName = $"parsen-docs-{settings.Name}-{Account}",

            BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
            ObjectOwnership = ObjectOwnership.BUCKET_OWNER_ENFORCED,
            EnforceSSL = true,
            MinimumTLSVersion = 1.2,

            Encryption = BucketEncryption.KMS,
            EncryptionKey = key,

            // Reduz em ~99% as chamadas (cobradas) ao KMS
            BucketKeyEnabled = true,

            // Sem versionamento: exclusão precisa ser irreversível (LGPD)
            Versioned = false,

            LifecycleRules = [.. retentionRules, abortIncompleteUploads],

            RemovalPolicy = RemovalPolicy.RETAIN,
        });
    }

    private TableV2 CreateTable(StageSettings settings) => new(this, "Table", new TablePropsV2
    {
        TableName = $"parsen-{settings.Name}",

        // Chave primária genérica: o formato do valor diz o tipo do item (tabela única)
        PartitionKey = new Attribute { Name = "PK", Type = AttributeType.STRING },
        SortKey = new Attribute { Name = "SK", Type = AttributeType.STRING },

        // Sob demanda: paga por requisição, custo ~zero sem uso
        Billing = Billing.OnDemand(),

        GlobalSecondaryIndexes =
        [
            // Fila do backoffice: TENANT#{id}#STATUS#{status} + createdAt
            new GlobalSecondaryIndexPropsV2
            {
                IndexName = "GSI1",
                PartitionKey = new Attribute { Name = "GSI1PK", Type = AttributeType.STRING },
                SortKey = new Attribute { Name = "GSI1SK", Type = AttributeType.STRING },
            },
            // Busca pela referência do cliente: TENANT#{id}#REF#{externalRef}
            new GlobalSecondaryIndexPropsV2
            {
                IndexName = "GSI2",
                PartitionKey = new Attribute { Name = "GSI2PK", Type = AttributeType.STRING },
                SortKey = new Attribute { Name = "GSI2SK", Type = AttributeType.STRING },
            },
        ],

        // Itens com ExpiresAt (epoch em segundos) no passado são apagados sem custo (ex.: idempotência)
        TimeToLiveAttribute = "ExpiresAt",

        // Backup contínuo: restauração a qualquer segundo dos últimos 35 dias
        PointInTimeRecoverySpecification = new PointInTimeRecoverySpecification
        {
            PointInTimeRecoveryEnabled = true,
        },

        DeletionProtection = settings.IsProduction,

        // Apagar a stack nunca apaga a tabela
        RemovalPolicy = RemovalPolicy.RETAIN,
    });
}
