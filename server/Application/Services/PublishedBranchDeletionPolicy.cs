using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

public enum BranchDeletionDecisionKind { NotEligible = 0, Eligible = 1, Deferred = 2, Kept = 3 }

/// <summary>CARD-0824 S2. <see cref="BranchDeletionDecisionKind.NotEligible"/> writes nothing; Deferred and Kept are recorded.</summary>
public sealed record BranchDeletionDecision(BranchDeletionDecisionKind Kind, string Reason);

/// <summary>
/// CARD-0824 S2 (plan §3.10, S2 test design B-1..B-5). The pure eligibility policy of one landing row, before any git
/// or runner I/O: the flag, the trigger (<see cref="AgentTaskLandingState.HasPublication"/>), the backlog cutoff, schema 3,
/// no recovery or adoption, a delegate branch name (never master, rc/* or any other branch), the source identity the
/// lease is taken on, the delay from <c>RemoteConfirmedAt</c>, and the runner volume allow-list.
/// </summary>
public static class PublishedBranchDeletionPolicy
{
    private static readonly AgentTaskLandingState State = new();

    public static BranchDeletionDecision Decide(AgentTaskLanding op, LandingSettings settings,
        IReadOnlyCollection<string> runnerIds, DateTime now)
    {
        if (!settings.DeleteSourceBranchAfterPublication) return NotEligible("flag_off");
        if (op.BranchDeletionState is LandBranchDeletionState.Deleted or LandBranchDeletionState.Kept)
            return NotEligible("already_settled");
        if (!State.HasPublication(op)) return NotEligible("not_published");
        var confirmed = op.RemoteConfirmedAt!.Value;
        if (settings.SourceBranchDeletionAfterUtc is not { } cutoff || confirmed <= cutoff.UtcDateTime)
            return NotEligible("before_cutoff");
        if (op.SchemaVersion != 3) return Kept("legacy_schema");
        if (op.RecoveryMode != LandRecoveryMode.None) return Kept("recovery_land");
        if (!IsDelegateBranch(op.SourceFullRef, op.TargetFullRef)) return Kept("protected_branch");
        // B-1: schema 3 never moves the branch, so origin still holds the source the land observed and rechecked.
        if (!GitObjectId.IsFull(op.SourceRemoteSha) || op.SourceRemoteSha != op.ReviewedSourceSha
            || op.OriginalSourceSha != op.ReviewedSourceSha || op.SourceRemoteFingerprint is not { Length: 64 })
            return Kept("source_identity_mismatch");
        if (now < confirmed.AddMinutes(Math.Max(0, settings.SourceBranchDeleteDelayMinutes)))
            return NotEligible("before_delay");
        var enabled = settings.SourceBranchDeletionRunnerIds ?? [];
        if (runnerIds.Any(runner => !enabled.Contains(runner, StringComparer.Ordinal)))
            return new(BranchDeletionDecisionKind.Deferred, "runner_volume_not_enabled");
        return new(BranchDeletionDecisionKind.Eligible, "eligible");
    }

    /// <summary>Only a delegate task branch (<c>feat/card-task-&lt;8hex&gt;</c> or <c>feat/card-&lt;n&gt;-&lt;stage&gt;-&lt;8hex&gt;</c>), never the target.</summary>
    public static bool IsDelegateBranch(string sourceFullRef, string targetFullRef) =>
        sourceFullRef.StartsWith("refs/heads/", StringComparison.Ordinal)
        && sourceFullRef != targetFullRef
        && DelegateBranchName.TryParse(sourceFullRef, out _);

    private static BranchDeletionDecision NotEligible(string reason) => new(BranchDeletionDecisionKind.NotEligible, reason);
    private static BranchDeletionDecision Kept(string reason) => new(BranchDeletionDecisionKind.Kept, reason);
}
