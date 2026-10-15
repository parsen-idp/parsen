using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Parsen.Api;
using Parsen.Api.Dossiers;
using Parsen.Application.Dossiers;
using Parsen.Infrastructure.Dossiers;

var builder = WebApplication.CreateBuilder(args);

// JSON da API: chaves e enums em snake_case (analysis_type, needs_review)
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
});

// Erros sempre em Problem Details, com o campo "code"
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions.TryAdd(ApiProblems.CodeKey, ApiProblems.DefaultCode(context.ProblemDetails.Status)));
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

// Autenticação por JWT: em dev, tokens do dotnet user-jwts; na AWS, Cognito (passo 5c)
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddAuthorization();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IDossierRepository, InMemoryDossierRepository>();
builder.Services.AddScoped<CreateDossierHandler>();
builder.Services.AddScoped<GetDossierHandler>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapDossierEndpoints();

app.Run();
