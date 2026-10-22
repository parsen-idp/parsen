namespace Parsen.Domain.Dossiers;

/// <summary>Tipos de documento do MVP (spec, seção 2). Outro: guardado e sinalizado na revisão, sem extração.</summary>
public enum DocumentType
{
    Holerite,
    ExtratoBancario,
    ComprovanteResidencia,
    MatriculaImovel,
    Outro,
}

/// <summary>
/// Códigos dos tipos em português snake_case (spec, convenções): os mesmos na API, no DynamoDB, nos schemas
/// (holerite.v2) e na resposta do modelo. Mapeamento explícito: renomear o enum não muda o código gravado.
/// </summary>
public static class DocumentTypeCodes
{
    private static readonly Dictionary<DocumentType, string> Codes = new()
    {
        [DocumentType.Holerite] = "holerite",
        [DocumentType.ExtratoBancario] = "extrato_bancario",
        [DocumentType.ComprovanteResidencia] = "comprovante_residencia",
        [DocumentType.MatriculaImovel] = "matricula_imovel",
        [DocumentType.Outro] = "outro",
    };

    public static IReadOnlyCollection<string> All => Codes.Values;

    public static string ToCode(DocumentType type) =>
        Codes.TryGetValue(type, out var code) ? code : throw new ArgumentOutOfRangeException(nameof(type), type, null);

    public static bool TryParse(string? code, out DocumentType type)
    {
        foreach (var (key, value) in Codes)
        {
            if (value == code)
            {
                type = key;
                return true;
            }
        }

        type = default;
        return false;
    }
}

public enum DocumentStatus
{
    /// <summary>Upload pedido; ainda não classificado.</summary>
    Pending,

    /// <summary>Tipo e páginas conhecidos; aguardando extração.</summary>
    Classified,

    /// <summary>Arquivo com vários documentos: os dados estão nos documentos filhos, não neste.</summary>
    Split,

    /// <summary>Campos extraídos.</summary>
    Extracted,
}

/// <summary>Faixa de páginas no arquivo enviado (1 = primeira), inclusive nas duas pontas.</summary>
public readonly record struct PageRange
{
    public PageRange(int first, int last)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(first, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(last, first);
        First = first;
        Last = last;
    }

    public int First { get; }

    public int Last { get; }

    public int Count => Last - First + 1;

    public bool Contains(int page) => page >= First && page <= Last;
}

/// <summary>Ação não permitida no status atual do documento.</summary>
public sealed class InvalidDocumentTransitionException : InvalidOperationException
{
    public InvalidDocumentTransitionException(DocumentStatus from, string action)
        : base($"A ação '{action}' não é permitida para um documento em '{from}'.")
    {
        From = from;
        Action = action;
    }

    public DocumentStatus From { get; }

    public string Action { get; }
}
