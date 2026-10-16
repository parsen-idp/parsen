using System.Text.Json;
using System.Text.Json.Nodes;
using Amazon.DynamoDBv2;
using Amazon.Lambda.Core;
using Amazon.Lambda.RuntimeSupport;
using Parsen.Auth.PreTokenGeneration;

// Inicialização fora do handler: roda uma vez por instância da Lambda (cold start) e é reaproveitada
var tableName = Environment.GetEnvironmentVariable("TABLE_NAME")
    ?? throw new InvalidOperationException("Variável TABLE_NAME não definida.");
var enricher = new TokenEnricher(new DynamoDbTenantLookup(new AmazonDynamoDBClient(), tableName));

await LambdaBootstrapBuilder.Create(HandleAsync).Build().RunAsync();

async Task<Stream> HandleAsync(Stream input, ILambdaContext context)
{
    var cognitoEvent = await JsonNode.ParseAsync(input)
        ?? throw new InvalidOperationException("Evento vazio.");

    // Só IDs e o tipo do gatilho no log: nada de e-mail, nome ou atributos do usuário (regra 3)
    var trigger = (string?)cognitoEvent["triggerSource"];
    var clientId = (string?)cognitoEvent["callerContext"]?["clientId"];

    using var cancellation = new CancellationTokenSource(context.RemainingTime - TimeSpan.FromMilliseconds(500));
    try
    {
        var result = await enricher.EnrichAsync(cognitoEvent, cancellation.Token);
        context.Logger.LogInformation($"Token enriquecido: trigger={trigger} client={clientId}");

        var output = new MemoryStream();
        await using (var writer = new Utf8JsonWriter(output))
        {
            result.WriteTo(writer);
        }

        output.Position = 0;
        return output;
    }
    catch (TenantNotFoundException)
    {
        context.Logger.LogWarning($"Token recusado, sem tenant: trigger={trigger} client={clientId}");
        throw;
    }
}
