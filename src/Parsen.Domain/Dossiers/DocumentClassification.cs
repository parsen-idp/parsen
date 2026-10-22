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
