using Microsoft.Extensions.Time.Testing;
using Parsen.Domain.Dossiers;
using Parsen.Domain.Tenants;

namespace Parsen.Domain.Tests.Dossiers;

public class DossierTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly TenantId Tenant = TenantId.From("ten_teste");

    private readonly FakeTimeProvider _clock = new(Start);

    private Dossier NewDossier(string? externalRef = null) =>
        Dossier.Create(Tenant, AnalysisType.Income, externalRef, _clock);

    // ---------- Criação ----------

    [Fact]
    public void Create_StartsAsDraftWithUtcTimestamps()
    {
        var dossier = NewDossier("proposta-42");

        Assert.Equal(DossierStatus.Draft, dossier.Status);
        Assert.Equal(Tenant, dossier.TenantId);
        Assert.Equal(AnalysisType.Income, dossier.AnalysisType);
        Assert.Equal("proposta-42", dossier.ExternalRef);
        Assert.Equal(Start, dossier.CreatedAt);
        Assert.Equal(TimeSpan.Zero, dossier.CreatedAt.Offset);
        Assert.Equal(Start, dossier.UpdatedAt);
    }

    [Fact]
    public void Create_ExternalRefIsOptional()
    {
        Assert.Null(NewDossier().ExternalRef);
    }

    // ---------- Reconstrução a partir da persistência ----------

    [Fact]
    public void Restore_RebuildsAllStateWithoutTransitions()
    {
        var id = DossierId.New();
        var updatedAt = Start.AddHours(2);

        var dossier = Dossier.Restore(
            id, Tenant, AnalysisType.Property, "ref-1", DossierStatus.Rejected,
            Start, updatedAt, "usr_analista", "Documento ilegível", version: 7);

        Assert.Equal(id, dossier.Id);
        Assert.Equal(Tenant, dossier.TenantId);
        Assert.Equal(AnalysisType.Property, dossier.AnalysisType);
        Assert.Equal("ref-1", dossier.ExternalRef);
        Assert.Equal(DossierStatus.Rejected, dossier.Status);
        Assert.Equal(Start, dossier.CreatedAt);
        Assert.Equal(updatedAt, dossier.UpdatedAt);
        Assert.Equal("usr_analista", dossier.DecidedBy);
        Assert.Equal("Documento ilegível", dossier.RejectionReason);
        Assert.Equal(7, dossier.Version);
    }

    [Fact]
    public void Create_StartsAtVersionOne()
    {
        Assert.Equal(1, NewDossier().Version);
    }

    // ---------- Transições válidas ----------

    [Fact]
    public void Submit_FromDraft_GoesToProcessing()
    {
        var dossier = NewDossier();
        _clock.Advance(TimeSpan.FromMinutes(1));

        dossier.Submit(_clock);

        Assert.Equal(DossierStatus.Processing, dossier.Status);
        Assert.Equal(Start.AddMinutes(1), dossier.UpdatedAt);
    }

    [Theory]
    [InlineData(VerificationOutcome.AllPassed, DossierStatus.Ready)]
    [InlineData(VerificationOutcome.RequiresReview, DossierStatus.NeedsReview)]
    public void CompleteVerification_FromProcessing_SetsStatusByOutcome(VerificationOutcome outcome, DossierStatus expected)
    {
        var dossier = InStatus(DossierStatus.Processing);

        dossier.CompleteVerification(outcome, _clock);

        Assert.Equal(expected, dossier.Status);
    }

    [Fact]
    public void Fail_FromProcessing_GoesToFailed()
    {
        var dossier = InStatus(DossierStatus.Processing);

        dossier.Fail(_clock);

        Assert.Equal(DossierStatus.Failed, dossier.Status);
    }

    [Fact]
    public void Retry_FromFailed_GoesBackToProcessing()
    {
        var dossier = InStatus(DossierStatus.Failed);

        dossier.Retry(_clock);

        Assert.Equal(DossierStatus.Processing, dossier.Status);
    }

    [Theory]
    [InlineData(DossierStatus.NeedsReview, VerificationOutcome.AllPassed, DossierStatus.Ready)]
    [InlineData(DossierStatus.Ready, VerificationOutcome.RequiresReview, DossierStatus.NeedsReview)]
    [InlineData(DossierStatus.Ready, VerificationOutcome.AllPassed, DossierStatus.Ready)]
    public void Reverify_AfterCorrection_RecalculatesStatus(DossierStatus from, VerificationOutcome outcome, DossierStatus expected)
    {
        var dossier = InStatus(from);

        dossier.Reverify(outcome, _clock);

        Assert.Equal(expected, dossier.Status);
    }

    [Theory]
    [InlineData(DossierStatus.Ready)]
    [InlineData(DossierStatus.NeedsReview)]
    public void Approve_FromReviewableStatus_RecordsWhoApproved(DossierStatus from)
    {
        var dossier = InStatus(from);

        dossier.Approve("usr_analista", _clock);

        Assert.Equal(DossierStatus.Approved, dossier.Status);
        Assert.Equal("usr_analista", dossier.DecidedBy);
        Assert.Null(dossier.RejectionReason);
    }

    [Theory]
    [InlineData(DossierStatus.Ready)]
    [InlineData(DossierStatus.NeedsReview)]
    public void Reject_FromReviewableStatus_RecordsWhoAndWhy(DossierStatus from)
    {
        var dossier = InStatus(from);

        dossier.Reject("usr_analista", "Documento ilegível", _clock);

        Assert.Equal(DossierStatus.Rejected, dossier.Status);
        Assert.Equal("usr_analista", dossier.DecidedBy);
        Assert.Equal("Documento ilegível", dossier.RejectionReason);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("")]
    public void Reject_WithoutReason_Throws(string reason)
    {
        var dossier = InStatus(DossierStatus.NeedsReview);

        Assert.Throws<ArgumentException>(() => dossier.Reject("usr_analista", reason, _clock));
    }

    // ---------- Transições inválidas ----------

    public static TheoryData<DossierStatus, string> InvalidTransitions => new()
    {
        { DossierStatus.Processing, nameof(Dossier.Submit) },
        { DossierStatus.Ready, nameof(Dossier.Submit) },
        { DossierStatus.Draft, nameof(Dossier.CompleteVerification) },
        { DossierStatus.Ready, nameof(Dossier.CompleteVerification) },
        { DossierStatus.Draft, nameof(Dossier.Fail) },
        { DossierStatus.Ready, nameof(Dossier.Fail) },
        { DossierStatus.Draft, nameof(Dossier.Retry) },
        { DossierStatus.Processing, nameof(Dossier.Retry) },
        { DossierStatus.Processing, nameof(Dossier.Reverify) },
        { DossierStatus.Approved, nameof(Dossier.Reverify) },
        { DossierStatus.Draft, nameof(Dossier.Approve) },
        { DossierStatus.Processing, nameof(Dossier.Approve) },
        { DossierStatus.Failed, nameof(Dossier.Approve) },
        { DossierStatus.Approved, nameof(Dossier.Approve) },
        { DossierStatus.Rejected, nameof(Dossier.Approve) },
        { DossierStatus.Draft, nameof(Dossier.Reject) },
        { DossierStatus.Approved, nameof(Dossier.Reject) },
        { DossierStatus.Rejected, nameof(Dossier.Reject) },
    };

    [Theory]
    [MemberData(nameof(InvalidTransitions))]
    public void InvalidTransition_ThrowsAndKeepsStatus(DossierStatus from, string action)
    {
        var dossier = InStatus(from);
        var updatedAt = dossier.UpdatedAt;
        _clock.Advance(TimeSpan.FromMinutes(1));

        var ex = Assert.Throws<InvalidDossierTransitionException>(() => Apply(dossier, action));

        Assert.Equal(from, ex.From);
        Assert.Equal(action, ex.Action);
        Assert.Equal(from, dossier.Status);
        Assert.Equal(updatedAt, dossier.UpdatedAt);
    }

    // ---------- Auxiliares ----------

    private void Apply(Dossier dossier, string action)
    {
        switch (action)
        {
            case nameof(Dossier.Submit): dossier.Submit(_clock); break;
            case nameof(Dossier.CompleteVerification): dossier.CompleteVerification(VerificationOutcome.AllPassed, _clock); break;
            case nameof(Dossier.Fail): dossier.Fail(_clock); break;
            case nameof(Dossier.Retry): dossier.Retry(_clock); break;
            case nameof(Dossier.Reverify): dossier.Reverify(VerificationOutcome.AllPassed, _clock); break;
            case nameof(Dossier.Approve): dossier.Approve("usr_analista", _clock); break;
            case nameof(Dossier.Reject): dossier.Reject("usr_analista", "motivo", _clock); break;
            default: throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }

    /// <summary>Leva um dossiê novo até o status pedido usando só transições válidas.</summary>
    private Dossier InStatus(DossierStatus status)
    {
        var dossier = NewDossier();
        if (status == DossierStatus.Draft)
        {
            return dossier;
        }

        dossier.Submit(_clock);
        switch (status)
        {
            case DossierStatus.Processing: break;
            case DossierStatus.Failed: dossier.Fail(_clock); break;
            case DossierStatus.Ready: dossier.CompleteVerification(VerificationOutcome.AllPassed, _clock); break;
            case DossierStatus.NeedsReview: dossier.CompleteVerification(VerificationOutcome.RequiresReview, _clock); break;
            case DossierStatus.Approved:
                dossier.CompleteVerification(VerificationOutcome.AllPassed, _clock);
                dossier.Approve("usr_analista", _clock);
                break;
            case DossierStatus.Rejected:
                dossier.CompleteVerification(VerificationOutcome.AllPassed, _clock);
                dossier.Reject("usr_analista", "motivo", _clock);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(status), status, null);
        }

        return dossier;
    }
}
