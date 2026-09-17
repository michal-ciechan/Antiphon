namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// CARD-0544 D-7. Bounded read of the deployment-owned qualification receipt plus the saved nightly
/// monitor. The only external dependency of interim admission; the policy logic itself is concrete.
/// </summary>
public interface IInterimVerificationReadinessReader
{
    Task<InterimReadiness> ReadAsync(string? repositoryPath, Guid? projectId, CancellationToken ct);
}

/// <summary>Readiness verdict. <see cref="Reason"/> is a stable code when not ready.</summary>
public sealed record InterimReadiness(bool Ready, string Reason, InterimReadinessSnapshot? Snapshot)
{
    public static InterimReadiness Unready(string reason) => new(false, reason, null);
}

/// <summary>The identities an admission snapshots so a later reader can tell which evidence it trusted.</summary>
public sealed record InterimReadinessSnapshot(
    string QualificationArtifactPath,
    string QualificationArtifactCommitSha,
    string PolicyHash,
    string ScriptHash,
    string ScheduledRunId,
    string WindmillJobId,
    IReadOnlyList<string> RecipientEvidenceIds,
    DateTime MonitorRecordedAt,
    DateTime ReadAt);
