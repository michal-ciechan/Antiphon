namespace Antiphon.Server.Domain.Enums;

/// <summary>CARD-0272. The question a pipeline step answers, independent of who answers it.</summary>
public enum OrchestrationStage
{
    /// <summary>Does the branch still apply cleanly on its target? Found = a conflict.</summary>
    Rebase = 0,
    /// <summary>Does the (rebased) work still build and pass? Found = red build or tests.</summary>
    Verify = 1,
    /// <summary>Is anything left behind — worktree, branch, bin-*, untracked junk? Found = something removed or fixed.</summary>
    Cleanup = 2,
    /// <summary>Is the work correct and complete against its plan? Found = a defect that needs a change.</summary>
    Review = 3,
    /// <summary>Did going back to the same delegate change the answer? Found = a material correction or addition.</summary>
    FollowUp = 4,
    /// <summary>Did the deploy verdict catch something? Found = a failed or partial verdict with a real cause.</summary>
    Deploy = 5,
}

/// <summary>
/// CARD-0272 role → stage defaults. Distinct from <see cref="AgentTaskRoles.IsStage"/> (CARD-0146
/// pipeline seats: Investigate/Plan/TestDesign/Code/Review). Do not unify the two vocabularies.
/// </summary>
public static class OrchestrationStages
{
    /// <summary>
    /// Role → default stage when <c>-Stage</c> is omitted. A follow-up wins over the role.
    /// Code, Plan, and every other role → null (not a stage run).
    /// </summary>
    public static OrchestrationStage? DefaultFor(AgentTaskRole role, bool followUp) =>
        followUp
            ? OrchestrationStage.FollowUp
            : role switch
            {
                AgentTaskRole.Review => OrchestrationStage.Review,
                AgentTaskRole.Test => OrchestrationStage.Verify,
                AgentTaskRole.Merge => OrchestrationStage.Rebase,
                AgentTaskRole.Deploy => OrchestrationStage.Deploy,
                _ => null,
            };
}

/// <summary>
/// What a stage run produced. Hit rate is Found / (Found + Clean); Skipped, Failed and Unreported
/// are counted in runs and excluded from the denominator.
/// </summary>
public enum StageOutcomeKind
{
    Clean = 0,
    Found = 1,
    Skipped = 2,
    Failed = 3,
    Unreported = 4,
}

/// <summary>Who wrote the row. Server and Backfill are automatic; Delegate and Orchestrator are S2/S3.</summary>
public enum StageOutcomeSource
{
    Server = 0,
    Delegate = 1,
    Orchestrator = 2,
    Backfill = 3,
}
