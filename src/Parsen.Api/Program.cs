using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Parsen.Api;
using Parsen.Api.Auth;
using Parsen.Api.Dossiers;
using Parsen.Application.Documents;
using Parsen.Application.Dossiers;
using Parsen.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Na Lambda, recebe os eventos do API Gateway REST; fora dela (dotnet run), não faz nada e o Kestrel atende
builder.Services.AddAWSLambdaHosting(LambdaEventSource.RestApi);

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

// Autenticação por JWT. Com Auth:CognitoIssuer, valida tokens do Cognito; sem ele, tokens do dotnet user-jwts (dev)
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => CognitoJwt.Configure(options, builder.Configuration["Auth:CognitoIssuer"]));
Scopes.AddPolicies(builder.Services.AddAuthorizationBuilder());

builder.Services.AddSingleton(TimeProvider.System);

// Persistence:Provider escolhe InMemory (padrão) ou DynamoDb
builder.Services.AddParsenPersistence(builder.Configuration);
builder.Services.AddScoped<CreateDossierHandler>();
builder.Services.AddScoped<GetDossierHandler>();
builder.Services.AddScoped<RequestUploadHandler>();
builder.Services.AddScoped<SubmitDossierHandler>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapDossierEndpoints();

app.Run();
