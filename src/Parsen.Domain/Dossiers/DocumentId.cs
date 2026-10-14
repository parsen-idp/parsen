using System.Diagnostics.CodeAnalysis;
using Parsen.Domain.Common;

namespace Parsen.Domain.Dossiers;

/// <summary>ID do documento: <c>doc_</c> + ULID.</summary>
public readonly record struct DocumentId
{
    private const string Prefix = "doc_";

    private readonly Ulid _value;

    private DocumentId(Ulid value) => _value = value;

    public static DocumentId New() => new(Ulid.NewUlid());

    public static DocumentId Parse(string value) =>
        TryParse(value, out var id) ? id : throw new FormatException($"ID de documento inválido: '{value}'.");

    public static bool TryParse([NotNullWhen(true)] string? value, out DocumentId id)
    {
        var ok = PrefixedUlid.TryParse(value, Prefix, out var ulid);
        id = ok ? new DocumentId(ulid) : default;
        return ok;
    }

    public override string ToString() => Prefix + _value;
}
