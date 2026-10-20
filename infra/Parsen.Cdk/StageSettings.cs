namespace Parsen.Cdk;

/// <summary>Configuração por estágio. Em dev, recursos podem ser destruídos; em prod, nunca.</summary>
internal sealed record StageSettings(string Name)
{
    public bool IsProduction => Name == "prod";

    /// <summary>O Textract não existe em sa-east-1 (spec, seção 6): o OCR roda nesta região.</summary>
    public string TextractRegion { get; init; } = "us-east-1";
}
