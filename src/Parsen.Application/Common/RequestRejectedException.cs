namespace Parsen.Application.Common;

/// <summary>Requisição recusada por regra da aplicação. A API responde 400 com o <see cref="Code"/>.</summary>
public sealed class RequestRejectedException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
