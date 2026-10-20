using Parsen.Domain.Dossiers;

namespace Parsen.Application.Pipeline;

/// <summary>Arquivos de um documento no armazenamento: o original enviado e as páginas renderizadas.</summary>
public interface IDocumentFiles
{
    Task<byte[]> ReadOriginalAsync(Document document, CancellationToken cancellationToken);

    /// <summary>Prazo da tag retention-days do original; as páginas herdam o mesmo prazo.</summary>
    Task<int?> GetRetentionDaysAsync(Document document, CancellationToken cancellationToken);

    Task SavePageAsync(Document document, int pageNumber, byte[] png, int retentionDays, CancellationToken cancellationToken);

    Task<byte[]> ReadPageAsync(Document document, int pageNumber, CancellationToken cancellationToken);

    /// <summary>Grava o resultado do OCR (textract.json) com a mesma retenção do original.</summary>
    Task SaveOcrAsync(Document document, OcrDocument ocr, int retentionDays, CancellationToken cancellationToken);
}

/// <summary>Transforma o arquivo original em uma imagem PNG por página.</summary>
public interface IPageRenderer
{
    /// <summary>Conta as páginas sem renderizar: permite recusar um arquivo grande demais antes do trabalho pesado.</summary>
    /// <exception cref="DocumentRejectedException">Arquivo que não pode ser processado (ex.: PDF com senha).</exception>
    int CountPages(byte[] content, DocumentContentType contentType);

    /// <exception cref="DocumentRejectedException">Arquivo que não pode ser processado (ex.: PDF com senha).</exception>
    IReadOnlyList<byte[]> Render(byte[] content, DocumentContentType contentType);
}

/// <summary>
/// Documento recusado por alguma etapa do pipeline por regra, não por falha técnica (ex.: PDF com senha,
/// imagem que o OCR não aceita): não adianta tentar de novo. O Step Functions não faz retry deste erro
/// (o nome da classe é o "errorType" que ele recebe) e encerra o dossiê.
/// </summary>
public sealed class DocumentRejectedException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
