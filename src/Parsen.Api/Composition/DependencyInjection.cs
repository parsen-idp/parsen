using Amazon.DynamoDBv2;
using Amazon.EventBridge;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Parsen.Application.Documents;
using Parsen.Application.Dossiers;
using Parsen.Infrastructure.Documents;
using Parsen.Infrastructure.Dossiers;
using Parsen.Infrastructure.DynamoDb;
using Parsen.Infrastructure.Events;
using Parsen.Infrastructure.Storage;

namespace Parsen.Api.Composition;

/// <summary>Composição da API: escolhe as implementações das portas a partir da configuração.</summary>
internal static class DependencyInjection
{
    /// <summary>
    /// Persistence:Provider = InMemory (padrão, desenvolvimento) ou DynamoDb (exige Persistence:TableName).
    /// Credenciais e região do DynamoDB vêm da seção AWS da configuração ou do ambiente (na Lambda, da role).
    /// </summary>
    public static IServiceCollection AddParsenPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Persistence:Provider"] ?? "InMemory";

        switch (provider)
        {
            case "InMemory":
                services.AddSingleton<IDossierRepository, InMemoryDossierRepository>();
                services.AddSingleton<IDocumentRepository, InMemoryDocumentRepository>();
                break;

            case "DynamoDb":
                var tableName = configuration["Persistence:TableName"]
                    ?? throw new InvalidOperationException("Persistence:TableName é obrigatório com o provider DynamoDb.");
                services.AddDefaultAWSOptions(configuration.GetAWSOptions());
                services.AddAWSService<IAmazonDynamoDB>();
                services.AddSingleton(new DynamoDbSettings(tableName));
                services.AddSingleton<IDossierRepository, DynamoDbDossierRepository>();
                services.AddSingleton<IDocumentRepository, DynamoDbDocumentRepository>();
                break;

            default:
                throw new InvalidOperationException($"Persistence:Provider desconhecido: '{provider}'.");
        }

        return services;
    }

    /// <summary>
    /// Armazenamento de arquivos (Storage:BucketName + Storage:KmsKeyId) e eventos (Events:BusName).
    /// Sem essas configurações, a API sobe normalmente e upload/submit respondem 503 INTEGRACAO_NAO_CONFIGURADA.
    /// </summary>
    public static IServiceCollection AddParsenIntegrations(this IServiceCollection services, IConfiguration configuration)
    {
        var bucketName = configuration["Storage:BucketName"];
        var kmsKeyId = configuration["Storage:KmsKeyId"];
        var busName = configuration["Events:BusName"];

        if (bucketName is not null || busName is not null)
        {
            services.AddDefaultAWSOptions(configuration.GetAWSOptions());
        }

        if (bucketName is not null)
        {
            services.AddAWSService<IAmazonS3>();
            services.AddSingleton(new S3StorageSettings(
                bucketName,
                kmsKeyId ?? throw new InvalidOperationException("Storage:KmsKeyId é obrigatório com Storage:BucketName.")));
            services.AddSingleton<IFileStorage, S3FileStorage>();
        }
        else
        {
            services.AddSingleton<IFileStorage, NotConfiguredFileStorage>();
        }

        if (busName is not null)
        {
            services.AddAWSService<IAmazonEventBridge>();
            services.AddSingleton(new EventBridgeSettings(busName));
            services.AddSingleton<IEventPublisher, EventBridgePublisher>();
        }
        else
        {
            services.AddSingleton<IEventPublisher, NotConfiguredEventPublisher>();
        }

        return services;
    }
}
