namespace Parsen.Domain.Dossiers;

/// <summary>Resultado consolidado das verificações: decide a fila, não a aprovação.</summary>
public enum VerificationOutcome
{
    /// <summary>Nenhuma verificação falhou e não há sinais.</summary>
    AllPassed,

    /// <summary>Alguma verificação falhou ou há algum sinal.</summary>
    RequiresReview,
}
