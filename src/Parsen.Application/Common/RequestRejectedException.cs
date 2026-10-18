namespace Parsen.Application.Common;

/// <summary>Por que a aplicação recusou a requisição. A API traduz para o status HTTP.</summary>
public enum RejectionKind
{
    /// <summary>Dados inválidos (400).</summary>
    Invalid,

    /// <summary>Recurso inexistente ou de outro tenant (404).</summary>
    NotFound,

    /// <summary>Estado atual não permite a operação (409).</summary>
    Conflict,

    /// <summary>Falha temporária em um serviço externo; o cliente pode tentar de novo (503).</summary>
    Unavailable,
}

/// <summary>Requisição recusada por regra da aplicação, com um <see cref="Code"/> estável para o cliente.</summary>
public sealed class RequestRejectedException(string code, string message, RejectionKind kind = RejectionKind.Invalid, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;

    public RejectionKind Kind { get; } = kind;
}
