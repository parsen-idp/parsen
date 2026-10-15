using Parsen.Domain.Tenants;

namespace Parsen.Domain.Dossiers;

/// <summary>
/// Dossiê: unidade principal de processamento. As transições de status seguem a tabela da spec (seção 7);
/// qualquer outra é recusada com <see cref="InvalidDossierTransitionException"/>.
/// </summary>
public sealed class Dossier
{
    private Dossier(DossierId id, TenantId tenantId, AnalysisType analysisType, string? externalRef, DateTimeOffset createdAt)
    {
        Id = id;
        TenantId = tenantId;
        AnalysisType = analysisType;
        ExternalRef = externalRef;
        Status = DossierStatus.Draft;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public DossierId Id { get; }

    public TenantId TenantId { get; }

    public AnalysisType AnalysisType { get; }

    /// <summary>ID do dossiê no sistema do cliente (opcional).</summary>
    public string? ExternalRef { get; }

    public DossierStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Usuário do cliente que aprovou ou rejeitou.</summary>
    public string? DecidedBy { get; private set; }

    public string? RejectionReason { get; private set; }

    /// <summary>Versão persistida, para controle otimista de concorrência. Começa em 1.</summary>
    public long Version { get; private set; } = 1;

    /// <summary>
    /// Reconstrói um dossiê já persistido. Não passa pela máquina de estados: o estado gravado
    /// já foi validado quando as transições aconteceram.
    /// </summary>
    public static Dossier Restore(
        DossierId id,
        TenantId tenantId,
        AnalysisType analysisType,
        string? externalRef,
        DossierStatus status,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        string? decidedBy,
        string? rejectionReason,
        long version)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);

        return new Dossier(id, tenantId, analysisType, externalRef, createdAt)
        {
            Status = status,
            UpdatedAt = updatedAt,
            DecidedBy = decidedBy,
            RejectionReason = rejectionReason,
            Version = version,
        };
    }

    public static Dossier Create(TenantId tenantId, AnalysisType analysisType, string? externalRef, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var reference = string.IsNullOrWhiteSpace(externalRef) ? null : externalRef.Trim();
        return new Dossier(DossierId.New(), tenantId, analysisType, reference, clock.GetUtcNow());
    }

    public void Submit(TimeProvider clock) =>
        Transition(nameof(Submit), DossierStatus.Processing, clock, DossierStatus.Draft);

    public void CompleteVerification(VerificationOutcome outcome, TimeProvider clock) =>
        Transition(nameof(CompleteVerification), ToStatus(outcome), clock, DossierStatus.Processing);

    public void Fail(TimeProvider clock) =>
        Transition(nameof(Fail), DossierStatus.Failed, clock, DossierStatus.Processing);

    public void Retry(TimeProvider clock) =>
        Transition(nameof(Retry), DossierStatus.Processing, clock, DossierStatus.Failed);

    /// <summary>Recalcula o status depois de uma correção de campo (verificações C# reexecutadas).</summary>
    public void Reverify(VerificationOutcome outcome, TimeProvider clock) =>
        Transition(nameof(Reverify), ToStatus(outcome), clock, DossierStatus.Ready, DossierStatus.NeedsReview);

    public void Approve(string approvedBy, TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(approvedBy);
        Transition(nameof(Approve), DossierStatus.Approved, clock, DossierStatus.Ready, DossierStatus.NeedsReview);
        DecidedBy = approvedBy;
    }

    public void Reject(string rejectedBy, string reason, TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rejectedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Transition(nameof(Reject), DossierStatus.Rejected, clock, DossierStatus.Ready, DossierStatus.NeedsReview);
        DecidedBy = rejectedBy;
        RejectionReason = reason.Trim();
    }

    private void Transition(string action, DossierStatus to, TimeProvider clock, params ReadOnlySpan<DossierStatus> allowedFrom)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (!allowedFrom.Contains(Status))
        {
            throw new InvalidDossierTransitionException(Status, action);
        }

        Status = to;
        UpdatedAt = clock.GetUtcNow();
    }

    private static DossierStatus ToStatus(VerificationOutcome outcome) => outcome switch
    {
        VerificationOutcome.AllPassed => DossierStatus.Ready,
        VerificationOutcome.RequiresReview => DossierStatus.NeedsReview,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };
}
