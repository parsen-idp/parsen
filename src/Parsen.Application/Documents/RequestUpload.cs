using Parsen.Application.Common;
using Parsen.Application.Dossiers;
using Parsen.Domain.Dossiers;
using Parsen.Domain.Tenants;

namespace Parsen.Application.Documents;

public sealed record RequestUploadCommand(TenantId TenantId, DossierId DossierId, string? ContentType, long SizeBytes);

public sealed record RequestUploadResult(Document Document, UploadTicket Ticket);

/// <summary>
/// Registra um documento no dossiê e autoriza o upload direto ao S3 (spec, seção 7).
/// Formato e tamanho declarados são conferidos aqui; o tamanho real, no submit.
/// </summary>
public sealed class RequestUploadHandler(
    IDossierRepository dossiers,
    IDocumentRepository documents,
    IFileStorage storage,
    TimeProvider clock)
{
    public async Task<RequestUploadResult> HandleAsync(RequestUploadCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!UploadLimits.TryParseContentType(command.ContentType, out var contentType))
        {
            throw new RequestRejectedException("FORMATO_NAO_SUPORTADO", "Formatos aceitos: PDF, JPG e PNG.");
        }

        if (command.SizeBytes <= 0)
        {
            throw new RequestRejectedException("REQUISICAO_INVALIDA", "O campo size_bytes deve ser maior que zero.");
        }

        if (command.SizeBytes > UploadLimits.MaxFileSizeBytes)
        {
            throw new RequestRejectedException("TAMANHO_EXCEDIDO", "O tamanho máximo por arquivo é 20 MB.");
        }

        var dossier = await dossiers.GetAsync(command.TenantId, command.DossierId, cancellationToken)
            ?? throw new RequestRejectedException("DOSSIE_NAO_ENCONTRADO", "Dossiê não encontrado.", RejectionKind.NotFound);

        if (dossier.Status != DossierStatus.Draft)
        {
            throw new RequestRejectedException(
                "DOSSIE_NAO_EDITAVEL", "Só é possível enviar documentos a um dossiê em rascunho.", RejectionKind.Conflict);
        }

        var document = Document.Create(command.TenantId, command.DossierId, contentType, command.SizeBytes, clock);
        await documents.AddAsync(document, cancellationToken);

        var ticket = await storage.CreateUploadAsync(document, cancellationToken);
        return new RequestUploadResult(document, ticket);
    }
}
