using System.Text.RegularExpressions;
using Parsen.Domain.Identifiers;

namespace Parsen.Application.Pipeline;

/// <summary>
/// CPF e CNPJ tirados do trecho do OCR, sem pontuação. O valor é o que está impresso (o OCR pode ter lido
/// um dígito errado): conferir o dígito verificador é uma verificação, não papel da extração.
/// Vale para todo campo cujo nome começa com "cpf" ou "cnpj" (ex.: cpf, cnpj_empregador).
/// </summary>
public static partial class IdentifierFromSource
{
    public static FieldValue? For(string fieldName, string sourceText)
    {
        ArgumentNullException.ThrowIfNull(fieldName);
        ArgumentNullException.ThrowIfNull(sourceText);

        if (fieldName.StartsWith("cnpj", StringComparison.Ordinal))
        {
            return Match(CnpjPattern(), sourceText, Cnpj.Normalize, Cnpj.Length);
        }

        if (fieldName.StartsWith("cpf", StringComparison.Ordinal))
        {
            return Match(CpfPattern(), sourceText, Cpf.Normalize, Cpf.Length);
        }

        return null;
    }

    private static FieldValue? Match(Regex pattern, string text, Func<string, string> normalize, int length)
    {
        var match = pattern.Match(text.ToUpperInvariant());
        if (!match.Success)
        {
            return null;
        }

        var normalized = normalize(match.Value);
        return normalized.Length == length ? FieldValue.Text(normalized) : null;
    }

    // 12 caracteres (letras ou dígitos, CNPJ alfanumérico) + 2 dígitos, com ou sem pontuação
    [GeneratedRegex(@"(?<![0-9A-Z])[0-9A-Z]{2}\.?[0-9A-Z]{3}\.?[0-9A-Z]{3}/?[0-9A-Z]{4}-?[0-9]{2}(?![0-9A-Z])")]
    private static partial Regex CnpjPattern();

    [GeneratedRegex(@"(?<![0-9])[0-9]{3}\.?[0-9]{3}\.?[0-9]{3}-?[0-9]{2}(?![0-9])")]
    private static partial Regex CpfPattern();
}
