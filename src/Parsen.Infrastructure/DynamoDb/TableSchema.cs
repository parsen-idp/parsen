namespace Parsen.Infrastructure.DynamoDb;

/// <summary>
/// Nomes do esquema da tabela única (spec, seção 9). Devem bater com a DataStack do CDK.
/// </summary>
public static class TableSchema
{
    public const string PartitionKey = "PK";
    public const string SortKey = "SK";

    /// <summary>Fila do backoffice: TENANT#{id}#STATUS#{status} + createdAt#id.</summary>
    public const string Gsi1 = "GSI1";
    public const string Gsi1PartitionKey = "GSI1PK";
    public const string Gsi1SortKey = "GSI1SK";

    /// <summary>Busca pela referência do cliente: TENANT#{id}#REF#{externalRef}.</summary>
    public const string Gsi2 = "GSI2";
    public const string Gsi2PartitionKey = "GSI2PK";
    public const string Gsi2SortKey = "GSI2SK";

    /// <summary>Itens com esse atributo (epoch em segundos) no passado são apagados pelo TTL.</summary>
    public const string ExpiresAt = "ExpiresAt";
}

/// <summary>Configuração da persistência no DynamoDB.</summary>
public sealed record DynamoDbSettings(string TableName);
