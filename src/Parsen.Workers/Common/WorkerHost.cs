using System.Text.Json;
using Amazon.Lambda.Core;
using Amazon.Lambda.RuntimeSupport;
using Amazon.Lambda.Serialization.SystemTextJson;

namespace Parsen.Workers.Common;

/// <summary>Inicia o loop da Lambda (runtime provided.al2023) com o JSON gerado em tempo de compilação.</summary>
public static class WorkerHost
{
    public static Task RunAsync<TInput, TOutput>(Func<TInput, ILambdaContext, Task<TOutput>> handler) =>
        LambdaBootstrapBuilder
            .Create(handler, CreateSerializer())
            .Build()
            .RunAsync();

    /// <summary>
    /// O serializador da Lambda cria o próprio contexto JSON com as opções dele e ignora o
    /// JsonSourceGenerationOptions do WorkersJsonContext: o snake_case precisa ser aplicado aqui.
    /// </summary>
    public static SourceGeneratorLambdaJsonSerializer<WorkersJsonContext> CreateSerializer() =>
        new(options => options.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower);

    /// <summary>Temperatura do modelo (EXTRACTION_TEMPERATURE); ausente = não enviar o parâmetro (Haiku 5.5).</summary>
    public static float? ModelTemperature() =>
        Environment.GetEnvironmentVariable("EXTRACTION_TEMPERATURE") is { Length: > 0 } value
            ? float.Parse(value, System.Globalization.CultureInfo.InvariantCulture)
            : null;

    public static string RequiredEnvironment(string name) =>
        Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException($"Variável {name} não definida.");
}
