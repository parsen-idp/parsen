using System.Text.RegularExpressions;
using Parsen.Domain.Identifiers;

namespace Parsen.Application.Pipeline;

/// <summary>
/// CPF, CNPJ e CEP tirados do trecho do OCR, sem pontuação. O valor é o que está impresso (o OCR pode ter lido
/// um dígito errado): conferir o dígito verificador é uma verificação, não papel da extração.
/// Vale para todo campo cujo nome começa com "cpf", "cnpj" ou "cep" (ex.: cpf, cnpj_empregador, cep).
/// </summary>
public static partial class IdentifierFromSource
{
    /// <summary>O campo é um identificador cujo valor sai do trecho do OCR (cpf*, cnpj*, cep*).</summary>
    public static bool Applies(string fieldName)
    {
        ArgumentNullException.ThrowIfNull(fieldName);
        return fieldName.StartsWith("cpf", StringComparison.Ordinal)
            || fieldName.StartsWith("cnpj", StringComparison.Ordinal)
            || fieldName.StartsWith("cep", StringComparison.Ordinal);
    }

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

        if (fieldName.StartsWith("cep", StringComparison.Ordinal))
        {
            return Match(CepPattern(), sourceText, DigitsOnly, 8);
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

    private static string DigitsOnly(string value) => new([.. value.Where(char.IsAsciiDigit)]);

    // 01234-567, 01.234-567 ou 01234567
    [GeneratedRegex(@"(?<![0-9])[0-9]{2}\.?[0-9]{3}-?[0-9]{3}(?![0-9])")]
    private static partial Regex CepPattern();
}
