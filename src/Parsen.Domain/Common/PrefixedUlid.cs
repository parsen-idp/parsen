using System.Diagnostics.CodeAnalysis;

namespace Parsen.Domain.Common;

/// <summary>Formato dos IDs públicos: prefixo do tipo + ULID (ex.: <c>dos_01J9Z...</c>).</summary>
internal static class PrefixedUlid
{
    private const int UlidLength = 26;

    public static bool TryParse([NotNullWhen(true)] string? value, string prefix, out Ulid ulid)
    {
        ulid = default;
        return value is not null
            && value.Length == prefix.Length + UlidLength
            && value.StartsWith(prefix, StringComparison.Ordinal)
            && Ulid.TryParse(value[prefix.Length..], out ulid);
    }
}
