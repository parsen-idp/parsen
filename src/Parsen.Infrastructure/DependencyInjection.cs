using Amazon.DynamoDBv2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Parsen.Application.Dossiers;
using Parsen.Infrastructure.Dossiers;
using Parsen.Infrastructure.DynamoDb;

namespace Parsen.Infrastructure;

public static class DependencyInjection
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
                break;

            case "DynamoDb":
                var tableName = configuration["Persistence:TableName"]
                    ?? throw new InvalidOperationException("Persistence:TableName é obrigatório com o provider DynamoDb.");
                services.AddDefaultAWSOptions(configuration.GetAWSOptions());
                services.AddAWSService<IAmazonDynamoDB>();
                services.AddSingleton(new DynamoDbSettings(tableName));
                services.AddSingleton<IDossierRepository, DynamoDbDossierRepository>();
                break;

            default:
                throw new InvalidOperationException($"Persistence:Provider desconhecido: '{provider}'.");
        }

        return services;
    }
}
