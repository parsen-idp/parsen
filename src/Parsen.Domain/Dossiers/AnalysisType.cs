namespace Parsen.Domain.Dossiers;

/// <summary>Tipo de análise do dossiê (spec, seção 7).</summary>
public enum AnalysisType
{
    /// <summary>Análise de renda: holerite, extrato, comprovante de residência.</summary>
    Income,

    /// <summary>Análise do imóvel: matrícula e proprietários.</summary>
    Property,

    /// <summary>Renda expressa; criado só por <c>POST /v1/income-checks</c>.</summary>
    IncomeCheck,
}
