using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Parsen.Infrastructure.DynamoDb;
using Testcontainers.DynamoDb;

namespace Parsen.Infrastructure.Tests.DynamoDb;

/// <summary>
/// Sobe o DynamoDB Local em Docker uma vez por classe de testes e cria a tabela com o mesmo
/// esquema da DataStack (PK/SK, GSI1, GSI2). Nada é enviado à AWS.
/// </summary>
public sealed class DynamoDbFixture : IAsyncLifetime
{
    public const string TableName = "parsen-test";

    // Imagem oficial da AWS com versão fixa: testes reproduzíveis
    private readonly DynamoDbContainer _container = new DynamoDbBuilder("amazon/dynamodb-local:3.1.0").Build();

    public IAmazonDynamoDB Client { get; private set; } = null!;

    public DynamoDbSettings Settings { get; } = new(TableName);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // O DynamoDB Local aceita qualquer credencial; a região só entra na assinatura da requisição
        Client = new AmazonDynamoDBClient(
            new BasicAWSCredentials("local", "local"),
            new AmazonDynamoDBConfig { ServiceURL = _container.GetConnectionString(), AuthenticationRegion = "sa-east-1" });

        await Client.CreateTableAsync(new CreateTableRequest
        {
            TableName = TableName,
            BillingMode = BillingMode.PAY_PER_REQUEST,
            AttributeDefinitions =
            [
                new(TableSchema.PartitionKey, ScalarAttributeType.S),
                new(TableSchema.SortKey, ScalarAttributeType.S),
                new(TableSchema.Gsi1PartitionKey, ScalarAttributeType.S),
                new(TableSchema.Gsi1SortKey, ScalarAttributeType.S),
                new(TableSchema.Gsi2PartitionKey, ScalarAttributeType.S),
                new(TableSchema.Gsi2SortKey, ScalarAttributeType.S),
            ],
            KeySchema =
            [
                new(TableSchema.PartitionKey, KeyType.HASH),
                new(TableSchema.SortKey, KeyType.RANGE),
            ],
            GlobalSecondaryIndexes =
            [
                Index(TableSchema.Gsi1, TableSchema.Gsi1PartitionKey, TableSchema.Gsi1SortKey),
                Index(TableSchema.Gsi2, TableSchema.Gsi2PartitionKey, TableSchema.Gsi2SortKey),
            ],
        });
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        await _container.DisposeAsync();
    }

    private static GlobalSecondaryIndex Index(string name, string partitionKey, string sortKey) => new()
    {
        IndexName = name,
        KeySchema = [new(partitionKey, KeyType.HASH), new(sortKey, KeyType.RANGE)],
        Projection = new Projection { ProjectionType = ProjectionType.ALL },
    };
}
