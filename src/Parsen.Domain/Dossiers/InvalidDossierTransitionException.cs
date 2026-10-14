namespace Parsen.Domain.Dossiers;

/// <summary>Ação não permitida no status atual do dossiê. A API responde <c>409 Conflict</c>.</summary>
public sealed class InvalidDossierTransitionException : InvalidOperationException
{
    public InvalidDossierTransitionException(DossierStatus from, string action)
        : base($"A ação '{action}' não é permitida para um dossiê em '{from}'.")
    {
        From = from;
        Action = action;
    }

    public DossierStatus From { get; }

    public string Action { get; }
}
