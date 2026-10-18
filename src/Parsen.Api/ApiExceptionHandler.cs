using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Parsen.Application.Common;
using Parsen.Domain.Dossiers;

namespace Parsen.Api;

/// <summary>Traduz exceções conhecidas em Problem Details. As demais seguem como 500.</summary>
internal sealed partial class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        // Falha temporária de serviço externo: o cliente recebe 503, e o motivo real fica só no log
        if (exception is RequestRejectedException { Kind: RejectionKind.Unavailable } unavailable)
        {
            LogUnavailable(logger, unavailable.Code, unavailable.InnerException);
        }

        // Mensagens de exceções de entrada não são devolvidas: podem conter trechos do corpo (dados pessoais)
        (int Status, string Code, string Title)? problem = exception switch
        {
            BadHttpRequestException bad => (bad.StatusCode, "REQUISICAO_INVALIDA", "Requisição inválida."),
            RequestRejectedException rejected => (ToStatus(rejected.Kind), rejected.Code, rejected.Message),
            InvalidDossierTransitionException => (StatusCodes.Status409Conflict, "TRANSICAO_INVALIDA", exception.Message),
            PersistenceConflictException => (StatusCodes.Status409Conflict, "CONFLITO", "O recurso foi alterado por outra requisição."),
            _ => null,
        };

        if (problem is not { } p)
        {
            return false;
        }

        httpContext.Response.StatusCode = p.Status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = p.Status,
                Title = p.Title,
                Extensions = { [ApiProblems.CodeKey] = p.Code },
            },
        });
    }

    private static int ToStatus(RejectionKind kind) => kind switch
    {
        RejectionKind.Invalid => StatusCodes.Status400BadRequest,
        RejectionKind.NotFound => StatusCodes.Status404NotFound,
        RejectionKind.Conflict => StatusCodes.Status409Conflict,
        RejectionKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status400BadRequest,
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Serviço externo indisponível ({Code})")]
    private static partial void LogUnavailable(ILogger logger, string code, Exception? exception);
}
