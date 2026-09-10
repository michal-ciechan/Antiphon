namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0475: the original 169 landing-matrix tuples at dc6af182, allocated to controlled vs real-Git.
/// </summary>
internal static class C475LegacyLandTuples
{
    public readonly record struct TupleRow(
        string OriginalClass, string OriginalMethod, string Arguments,
        string DestinationClass, string DestinationMethod, string Layer);

    public static IReadOnlyList<TupleRow> All { get; } = Build();

    private static List<TupleRow> Build()
    {
        var rows = new List<TupleRow>(169);
        var mutations = new[] { "advance", "dirty", "staged", "untracked", "switch", "metadata", "metadata-path", "metadata-target", "metadata-repository" };
        var later = new[] { "BeforeRebaseIntent", "RebaseStarted", "Prepared", "Verified", "TargetAdvanceStarted", "LocalTargetAdvanced", "BeforePushIntent", "PushStarted" };
        foreach (var m in mutations)
        {
            Add(rows, "AgentTaskLandBoundaryTests", "C448_V10_EachAcknowledgedBoundaryRechecksSource",
                $"remote,{m},False", "AgentTaskLandBoundaryControlledTests", "C448_V10_EachAcknowledgedBoundaryRechecksSource", "controlled");
            Add(rows, "AgentTaskLandBoundaryTests", "C448_V10_EachAcknowledgedBoundaryRechecksSource",
                $"remote,{m},True", "AgentTaskLandBoundaryControlledTests", "C448_V10_EachAcknowledgedBoundaryRechecksSource", "controlled");
        }
        foreach (var b in later)
            foreach (var m in mutations)
                Add(rows, "AgentTaskLandBoundaryTests", "C448_V10_EachAcknowledgedBoundaryRechecksSource",
                    $"{b},{m},False", "AgentTaskLandBoundaryControlledTests", "C448_V10_EachAcknowledgedBoundaryRechecksSource", "controlled");

        foreach (var change in new[] { "source-ref", "source-path", "target", "repository" })
            Add(rows, "AgentTaskLandBoundaryTests", "C448_V10_VerificationCannotFreezeOldTaskCoordinates",
                change, "AgentTaskLandBoundaryControlledTests", "C448_V10_VerificationCannotFreezeOldTaskCoordinates", "controlled");

        foreach (var change in new[] { "advance", "switch", "dirty", "staged" })
            Add(rows, "AgentTaskLandBoundaryTests", "C448_V11_TargetMutationAfterFastForwardCannotBeAcknowledged",
                change, "AgentTaskLandBoundaryTests", "C448_V11_TargetMutationAfterFastForwardCannotBeAcknowledged", "real");
        foreach (var name in new[] { "source", "target-before", "prepared" })
            foreach (var collision in new[] { "False", "True" })
                Add(rows, "AgentTaskLandBoundaryTests", "C448_V32_EachRecoveryPinFailureStopsDependentMutation",
                    $"{name},{collision}", "AgentTaskLandBoundaryTests", "C448_V32_EachRecoveryPinFailureStopsDependentMutation", "real");
        foreach (var co in new[] { "False", "True" })
            Add(rows, "AgentTaskLandBoundaryTests", "C448_V11_TargetAdvanceUsesTheCapturedCheckoutAndOldSha",
                co, "AgentTaskLandBoundaryTests", "C448_V11_TargetAdvanceUsesTheCapturedCheckoutAndOldSha", "real");
        Add(rows, "AgentTaskLandBoundaryTests", "C448_V32_RebaseConfigurationCannotStashOrRewriteUnrelatedRefs",
            "", "AgentTaskLandBoundaryTests", "C448_V32_RebaseConfigurationCannotStashOrRewriteUnrelatedRefs", "real");
        Add(rows, "AgentTaskLandBoundaryTests", "C448_V36_CreationUsesTheRepositoryLeaseAndThreadsNestedOwnership",
            "", "AgentTaskLandBoundaryTests", "C448_V36_CreationUsesTheRepositoryLeaseAndThreadsNestedOwnership", "real");
        foreach (var state in new[] { "MERGE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD", "rebase-merge", "rebase-apply", "sequencer" })
            Add(rows, "AgentTaskLandBoundaryTests", "C448_V11_TargetSequencerBlocksPreparation",
                state, "AgentTaskLandBoundaryTests", "C448_V11_TargetSequencerBlocksPreparation", "real");
        foreach (var pin in new[] { "source", "target-before", "prepared" })
            Add(rows, "AgentTaskLandBoundaryTests", "C448_V26_RecoveryPinsRemainPrerequisitesAfterVerification",
                pin, "AgentTaskLandBoundaryTests", "C448_V26_RecoveryPinsRemainPrerequisitesAfterVerification", "real");

        foreach (var writer in new[] { "shared", "follow-up" })
            foreach (var mode in new[] { "Fresh", "AlreadyPresent", "ResumePublication", "CleanupRetry" })
                foreach (var order in new[] { "land-first", "dispatch-first", "dispatch-before-acquire" })
                    Add(rows, "AgentTaskLandAdmissionTests", "C448_V14_RealDispatchAdmissionAndEveryLandModeExcludeEachOther",
                        $"{writer},{mode},{order}", "AgentTaskLandAdmissionControlledTests",
                        "C448_V14_DispatchAdmissionAndEveryLandModeExcludeEachOther", "controlled");

        foreach (var local in new[] { "False", "True" })
            Add(rows, "AgentTaskLandConcurrencyTests", "C448_V36_SettlementLeasePrecedesItsFirstMutation",
                local, "AgentTaskLandConcurrencyTests", "C448_V36_SettlementLeasePrecedesItsFirstMutation", "real");
        foreach (var landFirst in new[] { "True", "False" })
            foreach (var local in new[] { "False", "True" })
                Add(rows, "AgentTaskLandConcurrencyTests", "C448_V36_SettlementAndChildMergeExcludeLandingInBothOrders",
                    $"{landFirst},{local}", "AgentTaskLandConcurrencyTests", "C448_V36_SettlementAndChildMergeExcludeLandingInBothOrders", "real");

        foreach (var holder in new[] { "shared", "follow-up", "lease" })
            foreach (var mode in new[] { "AlreadyPresent", "Fresh", "ResumePublication", "CleanupRetry" })
                Add(rows, "AgentTaskLandConcurrencyTests", "C448_V14_EveryModeHonoursWriterAndLeaseHolds",
                    $"{holder},{mode}", "AgentTaskLandConcurrencyControlledTests",
                    "C448_V14_EveryModeHonoursWriterAndLeaseHolds", "controlled");

        foreach (var change in new[] { "source", "source-dirty", "source-switch", "source-staged", "source-untracked",
                     "source-registration", "target", "target-dirty", "target-staged", "target-switch" })
            Add(rows, "AgentTaskLandConcurrencyTests", "C448_V10_VerificationDoesNotAuthorizeChangedSourceOrTarget",
                change, "AgentTaskLandConcurrencyControlledTests",
                "C448_V10_VerificationDoesNotAuthorizeChangedSourceOrTarget", "controlled");

        return rows;
    }

    private static void Add(List<TupleRow> rows, string oc, string om, string args, string dc, string dm, string layer)
        => rows.Add(new(oc, om, args, dc, dm, layer));
}
