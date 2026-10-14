using System.Diagnostics.CodeAnalysis;
using Parsen.Domain.Common;

namespace Parsen.Domain.Dossiers;

/// <summary>ID do dossiê: <c>dos_</c> + ULID. Ordenável pelo momento de criação.</summary>
public readonly record struct DossierId
{
    private const string Prefix = "dos_";

    private readonly Ulid _value;

    private DossierId(Ulid value) => _value = value;

    public static DossierId New() => new(Ulid.NewUlid());

    public static DossierId Parse(string value) =>
        TryParse(value, out var id) ? id : throw new FormatException($"ID de dossiê inválido: '{value}'.");

    public static bool TryParse([NotNullWhen(true)] string? value, out DossierId id)
    {
        var ok = PrefixedUlid.TryParse(value, Prefix, out var ulid);
        id = ok ? new DossierId(ulid) : default;
        return ok;
    }

    public override string ToString() => Prefix + _value;
}
