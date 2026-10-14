namespace Parsen.Cdk;

/// <summary>Configuração por estágio. Em dev, recursos podem ser destruídos; em prod, nunca.</summary>
internal sealed record StageSettings(string Name)
{
    public bool IsProduction => Name == "prod";
}
