using Parsen.Domain.Dossiers;

namespace Parsen.Application.Pipeline;

/// <summary>Arquivos de um documento no armazenamento: o original enviado e as páginas renderizadas.</summary>
public interface IDocumentFiles
{
    Task<byte[]> ReadOriginalAsync(Document document, CancellationToken cancellationToken);

    /// <summary>Prazo da tag retention-days do original; as páginas herdam o mesmo prazo.</summary>
    Task<int?> GetRetentionDaysAsync(Document document, CancellationToken cancellationToken);

    Task SavePageAsync(Document document, int pageNumber, byte[] png, int retentionDays, CancellationToken cancellationToken);
}

/// <summary>Transforma o arquivo original em uma imagem PNG por página.</summary>
public interface IPageRenderer
{
    /// <summary>Conta as páginas sem renderizar: permite recusar um arquivo grande demais antes do trabalho pesado.</summary>
    /// <exception cref="IngestRejectedException">Arquivo que não pode ser processado (ex.: PDF com senha).</exception>
    int CountPages(byte[] content, DocumentContentType contentType);

    /// <exception cref="IngestRejectedException">Arquivo que não pode ser processado (ex.: PDF com senha).</exception>
    IReadOnlyList<byte[]> Render(byte[] content, DocumentContentType contentType);
}

/// <summary>
/// Documento recusado na ingestão por regra (não por falha técnica): não adianta tentar de novo.
/// O Step Functions não faz retry deste erro e encerra o dossiê.
/// </summary>
public sealed class IngestRejectedException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
