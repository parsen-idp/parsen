using Parsen.Application.Documents;
using Parsen.Domain.Dossiers;
using Parsen.Domain.Tenants;

namespace Parsen.Application.Pipeline;

/// <param name="Documents">Documentos lógicos do arquivo: ele mesmo (um documento) ou os filhos do split.</param>
/// <param name="Reused">Classificação já gravada (retry): o modelo não foi chamado de novo.</param>
public sealed record ClassificationResult(IReadOnlyList<Document> Documents, IReadOnlyList<string> Alerts, ModelUsage Usage, bool Reused);

/// <summary>
/// Etapa de split e classificação (spec, seções 5 e 14): uma chamada ao modelo por arquivo enviado. Um documento
/// só: o próprio arquivo é classificado. Vários: o arquivo vira origem e cada documento vira um filho.
/// </summary>
public sealed class ClassifyDocumentHandler(
    IDocumentRepository documents,
    IDocumentFiles files,
    IDocumentClassifier classifier,
    TimeProvider clock)
{
    public async Task<ClassificationResult> HandleAsync(
        TenantId tenantId, DossierId dossierId, DocumentId documentId, CancellationToken cancellationToken)
    {
        var file = await documents.GetAsync(tenantId, dossierId, documentId, cancellationToken)
            ?? throw new InvalidOperationException($"Documento {documentId} não encontrado no dossiê {dossierId}.");

        // Retry: a classificação já gravada vale; chamar o modelo de novo custaria e poderia mudar o split
        if (file.Status == DocumentStatus.Split)
        {
            var children = (await documents.ListByDossierAsync(tenantId, dossierId, cancellationToken))
                .Where(d => d.SourceDocumentId == file.Id)
                .OrderBy(d => d.Pages?.First)
                .ToList();
            return new ClassificationResult(children, [], ModelUsage.None, Reused: true);
        }

        if (file.Status != DocumentStatus.Pending)
        {
            return new ClassificationResult([file], [], ModelUsage.None, Reused: true);
        }

        var ocr = await files.ReadOcrAsync(file, cancellationToken);
        var output = await classifier.ClassifyAsync(ocr, cancellationToken);

        var alerts = output.Alerts.ToList();
        var parts = Validate(output.Parts, ocr.Pages.Count, alerts);

        IReadOnlyList<Document> result;
        if (parts.Count == 1)
        {
            file.Classify(parts[0].Type, parts[0].Pages);
            await documents.SaveClassificationAsync(file, [], cancellationToken);
            result = [file];
        }
        else
        {
            var children = file.Split([.. parts.Select(p => (p.Type, p.Pages))], clock);
            await documents.SaveClassificationAsync(file, children, cancellationToken);
            result = children;
        }

        return new ClassificationResult(result, alerts, output.Usage, Reused: false);
    }

    /// <summary>
    /// Faixas em ordem, sem sobreposição e dentro do arquivo. Correções seguras são aplicadas com alerta: faixa que
    /// passa do fim é cortada na última página; faixa que começa depois do fim é descartada. O que sobrar inválido
    /// (ex.: sobreposição) faz o arquivo inteiro virar um documento "outro": não trava o dossiê e leva o caso à revisão.
    /// </summary>
    private static List<ClassifiedPart> Validate(IReadOnlyList<ClassifiedPart> parts, int pageCount, List<string> alerts)
    {
        var inside = parts.Where(p => p.Pages.First <= pageCount).ToList();
        if (inside.Count < parts.Count)
        {
            alerts.Add("Faixa de páginas além do fim do arquivo descartada da classificação.");
        }

        var sorted = inside
            .Select(p => p.Pages.Last <= pageCount ? p : p with { Pages = new PageRange(p.Pages.First, pageCount) })
            .OrderBy(p => p.Pages.First)
            .ToList();
        if (sorted.Zip(inside.OrderBy(p => p.Pages.First)).Any(pair => pair.First.Pages != pair.Second.Pages))
        {
            alerts.Add("Faixa de páginas ajustada ao fim do arquivo na classificação.");
        }

        var valid = sorted.Count > 0
            && sorted.Zip(sorted.Skip(1)).All(pair => pair.Second.Pages.First > pair.First.Pages.Last);

        if (valid)
        {
            return sorted;
        }

        alerts.Add("Classificação inválida devolvida pelo modelo (faixas de páginas); o arquivo foi tratado como um documento só, de tipo desconhecido.");
        return [new ClassifiedPart(DocumentType.Outro, new PageRange(1, Math.Max(pageCount, 1)))];
    }
}
