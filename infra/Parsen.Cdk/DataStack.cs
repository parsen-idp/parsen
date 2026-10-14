using Amazon.CDK;
using Amazon.CDK.AWS.DynamoDB;
using Constructs;
using Attribute = Amazon.CDK.AWS.DynamoDB.Attribute;

namespace Parsen.Cdk;

/// <summary>Stack de dados: tabela DynamoDB, bucket S3 de documentos e chave KMS (spec, seção 9).</summary>
internal sealed class DataStack : Stack
{
    public DataStack(Construct scope, string id, IStackProps props, StageSettings settings)
        : base(scope, id, props)
    {
        Table = CreateTable(settings);
    }

    /// <summary>Tabela única da Parsen; toda PK começa com TENANT#{tenantId}.</summary>
    public TableV2 Table { get; }

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
