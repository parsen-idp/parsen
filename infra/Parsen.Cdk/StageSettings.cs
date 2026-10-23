namespace Parsen.Cdk;

/// <summary>Configuração por estágio. Em dev, recursos podem ser destruídos; em prod, nunca.</summary>
internal sealed record StageSettings(string Name)
{
    public bool IsProduction => Name == "prod";

    /// <summary>O Textract não existe em sa-east-1 (spec, seção 6): o OCR roda nesta região.</summary>
    public string TextractRegion { get; init; } = "us-east-1";

    /// <summary>
    /// Inference profile da extração (spec, seção 14): Haiku 4.5 Global enquanto a AWS não libera a cota
    /// do Haiku 5.5 (global.anthropic.claude-haiku-5-5) para a conta. Trocar só com o Parsen.Eval.
    /// </summary>
    public string ExtractionModelId { get; init; } = "global.anthropic.claude-haiku-4-5-20251001-v1:0";

    /// <summary>
    /// Temperatura da extração e da classificação: 0 deixa a resposta estável. Null no Haiku 5.5, que recusa valor
    /// diferente do padrão (trocar junto com o ExtractionModelId).
    /// </summary>
    public double? ExtractionTemperature { get; init; } = 0;

    /// <summary>Modelo base por trás do profile (sem o prefixo de roteamento "global." ou "us."): é ele que o IAM autoriza.</summary>
    public string ExtractionFoundationModelId => ExtractionModelId[(ExtractionModelId.IndexOf('.', StringComparison.Ordinal) + 1)..];
}
