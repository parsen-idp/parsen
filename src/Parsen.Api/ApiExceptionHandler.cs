using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Parsen.Application.Common;
using Parsen.Domain.Dossiers;

namespace Parsen.Api;

/// <summary>Traduz exceções conhecidas em Problem Details. As demais seguem como 500.</summary>
internal sealed class ApiExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        // Mensagens de exceções de entrada não são devolvidas: podem conter trechos do corpo (dados pessoais)
        (int Status, string Code, string Title)? problem = exception switch
        {
            BadHttpRequestException bad => (bad.StatusCode, "REQUISICAO_INVALIDA", "Requisição inválida."),
            RequestRejectedException rejected => (StatusCodes.Status400BadRequest, rejected.Code, rejected.Message),
            InvalidDossierTransitionException => (StatusCodes.Status409Conflict, "TRANSICAO_INVALIDA", exception.Message),
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
}
