using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;

namespace Antiphon.Server.Application.Services;

public enum BranchDeletionDecisionKind { NotEligible = 0, Eligible = 1, Deferred = 2, Kept = 3 }

/// <summary>CARD-0824 S2. <see cref="BranchDeletionDecisionKind.NotEligible"/> writes nothing; Deferred and Kept are recorded.</summary>
public sealed record BranchDeletionDecision(BranchDeletionDecisionKind Kind, string Reason);

/// <summary>CARD-0824 S2 (plan §3.10). The pure eligibility policy of one landing row. Stub: nothing is eligible.</summary>
public static class PublishedBranchDeletionPolicy
{
    public static BranchDeletionDecision Decide(AgentTaskLanding op, LandingSettings settings,
        IReadOnlyCollection<string> runnerIds, DateTime now) =>
        new(BranchDeletionDecisionKind.NotEligible, "not_implemented");
}
