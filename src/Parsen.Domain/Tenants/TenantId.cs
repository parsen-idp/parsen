namespace Parsen.Domain.Tenants;

/// <summary>Identificador do tenant (cliente da Parsen). Vem sempre do token, nunca do corpo da requisição.</summary>
public readonly record struct TenantId
{
    private TenantId(string value) => Value = value;

    public string Value { get; }

    public static TenantId From(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new TenantId(value);
    }

    public override string ToString() => Value;
}
