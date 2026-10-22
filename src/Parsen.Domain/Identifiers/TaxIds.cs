namespace Parsen.Domain.Identifiers;

/// <summary>CPF: 9 dígitos + 2 verificadores (módulo 11, pesos 10..2 e 11..2).</summary>
public static class Cpf
{
    public const int Length = 11;

    /// <summary>Só os dígitos (ex.: "529.982.247-25" → "52998224725").</summary>
    public static string Normalize(string value) => new([.. (value ?? "").Where(char.IsAsciiDigit)]);

    public static bool IsValid(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Any(c => !char.IsAsciiDigit(c) && c is not ('.' or '-' or ' ')))
        {
            return false;
        }

        var digits = Normalize(value);

        // 111.111.111-11 e afins passam no cálculo, mas não são emitidos
        return digits.Length == Length
            && digits.Distinct().Count() > 1
            && CheckDigits(digits[..9]) == digits[9..];
    }

    /// <summary>Os 2 dígitos verificadores dos 9 primeiros.</summary>
    public static string CheckDigits(string firstNine)
    {
        ArgumentNullException.ThrowIfNull(firstNine);
        if (firstNine.Length != 9 || !firstNine.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("A base do CPF tem 9 dígitos.", nameof(firstNine));
        }

        var first = Mod11(firstNine, weightStart: 10);
        var second = Mod11(firstNine + first, weightStart: 11);
        return $"{first}{second}";
    }

    private static int Mod11(string digits, int weightStart)
    {
        var sum = 0;
        for (var i = 0; i < digits.Length; i++)
        {
            sum += (digits[i] - '0') * (weightStart - i);
        }

        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }
}

/// <summary>
/// CNPJ: 12 caracteres (dígitos ou, desde julho de 2026, letras maiúsculas) + 2 dígitos verificadores.
/// Módulo 11 com pesos 2..9 da direita para a esquerda; cada caractere vale seu código ASCII − 48
/// (0–9 valem 0–9, A vale 17), o que mantém o cálculo dos CNPJ numéricos.
/// </summary>
public static class Cnpj
{
    public const int Length = 14;

    /// <summary>Letras e dígitos, em maiúsculas (ex.: "12.abc.345/01de-35" → "12ABC34501DE35").</summary>
    public static string Normalize(string value) => new([.. (value ?? "").Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant)]);

    public static bool IsValid(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '/' or '-' or ' ')))
        {
            return false;
        }

        var normalized = Normalize(value);
        return normalized.Length == Length
            && normalized[..12].All(char.IsAsciiLetterOrDigit)
            && normalized[12..].All(char.IsAsciiDigit)
            && normalized.Distinct().Count() > 1
            && CheckDigits(normalized[..12]) == normalized[12..];
    }

    /// <summary>Os 2 dígitos verificadores da base de 12 caracteres.</summary>
    public static string CheckDigits(string base12)
    {
        ArgumentNullException.ThrowIfNull(base12);
        if (base12.Length != 12 || !base12.All(c => char.IsAsciiDigit(c) || char.IsAsciiLetterUpper(c)))
        {
            throw new ArgumentException("A base do CNPJ tem 12 caracteres (dígitos ou letras maiúsculas).", nameof(base12));
        }

        var first = Mod11(base12);
        var second = Mod11(base12 + first);
        return $"{first}{second}";
    }

    private static int Mod11(string characters)
    {
        var sum = 0;
        for (var i = 0; i < characters.Length; i++)
        {
            // Pesos 2, 3, ..., 9, 2, 3... a partir do último caractere
            var weight = 2 + ((characters.Length - 1 - i) % 8);
            sum += (characters[i] - '0') * weight;
        }

        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }
}
