using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Parsen.Application.Documents;
using Parsen.Application.Dossiers;

namespace Parsen.Api.Tests.Infrastructure;

/// <summary>Sobe a API em memória com autenticação de teste e relógio controlado.</summary>
public sealed class ParsenApiFactory : WebApplicationFactory<Program>
{
    public static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    /// <summary>S3 simulado, compartilhado pelos testes da classe.</summary>
    public FakeFileStorage Files { get; } = new();

    /// <summary>EventBridge simulado, compartilhado pelos testes da classe.</summary>
    public FakeEventPublisher Events { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
            services.AddSingleton<IFileStorage>(Files);
            services.AddSingleton<IEventPublisher>(Events);

            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultScheme = TestAuthHandler.SchemeName;
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            });
        });
    }

    /// <summary>Cliente HTTP autenticado como o tenant informado, com os scopes pedidos (padrão: todos).</summary>
    public HttpClient CreateClientFor(string tenantId, string scopes = TestAuthHandler.AllScopes)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId);
        client.DefaultRequestHeaders.Add(TestAuthHandler.ScopesHeader, scopes);
        return client;
    }
}
