using Amazon.Lambda.Core;
using Amazon.Lambda.RuntimeSupport;
using Amazon.Lambda.Serialization.SystemTextJson;

namespace Parsen.Workers.Common;

/// <summary>Inicia o loop da Lambda (runtime provided.al2023) com o JSON gerado em tempo de compilação.</summary>
public static class WorkerHost
{
    public static Task RunAsync<TInput, TOutput>(Func<TInput, ILambdaContext, Task<TOutput>> handler) =>
        LambdaBootstrapBuilder
            .Create(handler, new SourceGeneratorLambdaJsonSerializer<WorkersJsonContext>())
            .Build()
            .RunAsync();

    public static string RequiredEnvironment(string name) =>
        Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException($"Variável {name} não definida.");
}
