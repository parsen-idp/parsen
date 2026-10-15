namespace Parsen.Application.Common;

/// <summary>
/// Escrita recusada pelo banco: o item já existe ou foi alterado por outra execução
/// (controle otimista de concorrência). A API responde 409.
/// </summary>
public sealed class PersistenceConflictException(string message, Exception? innerException = null)
    : Exception(message, innerException);
