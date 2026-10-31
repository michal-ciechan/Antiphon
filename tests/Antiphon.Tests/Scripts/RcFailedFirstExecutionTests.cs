using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcFailedFirstExecutionTests
{
    // Provisional full replay bound uses S2's measured 156-launch cost (CARD-1514).
    [Test, Timeout(1_140_000)]
    public Task ActualLoopFollowsSchedule(CancellationToken cancellationToken) =>
        ScriptHarness.RunHarnessCaseAsync("test-nightly-failed-first.ps1", "FF", nameof(ActualLoopFollowsSchedule), 18,
            ["FF ActualLoopFollowsSchedule.actualTrace", "FF ActualLoopFollowsSchedule.permutation",
             "FF ActualLoopFollowsSchedule.oneBuildDiscovery", "FF ActualLoopFollowsSchedule.historyBinding",
             "FF ActualLoopFollowsSchedule.allChunksVerdict", "FF ActualLoopFollowsSchedule.finalProjection",
             "FF ActualLoopFollowsSchedule.fallback", "FF ActualLoopFollowsSchedule.progressOrder",
             "FF ActualLoopFollowsSchedule.heartbeatCollision", "FF ActualLoopFollowsSchedule.heartbeatLegacy",
             "FF ActualLoopFollowsSchedule.heartbeatRefusals", "FF ActualLoopFollowsSchedule.heartbeatIo",
             "FF ActualLoopFollowsSchedule.heartbeatLiveness",
             "FF ActualLoopFollowsSchedule.chainProducer", "FF ActualLoopFollowsSchedule.chainPreviousOrder",
             "FF ActualLoopFollowsSchedule.chainRegrouped",
             "FF ActualLoopFollowsSchedule.chainDigestMissing", "FF ActualLoopFollowsSchedule.chainIngress"],
            new ScriptHarnessOptions(TimeSpan.FromSeconds(1080), TimeSpan.FromSeconds(10)), cancellationToken);

    // Actual entrypoint plus S2's measured ~55s D-15 matrix, shared setup (CARD-1514).
    [Test, Timeout(240_000)]
    public Task JournalCustodySurvivesInterleaving(CancellationToken cancellationToken) =>
        ScriptHarness.RunHarnessCaseAsync("test-nightly-failed-first.ps1", "FF", nameof(JournalCustodySurvivesInterleaving), 27,
            ["FF JournalCustodySurvivesInterleaving.interleaving", "FF JournalCustodySurvivesInterleaving.hostDiscovery",
             "FF JournalCustodySurvivesInterleaving.prefixState", "FF JournalCustodySurvivesInterleaving.journalBindings",
             "FF JournalCustodySurvivesInterleaving.oneContextClose", "FF JournalCustodySurvivesInterleaving.timeoutPermutation",
             "FF JournalCustodySurvivesInterleaving.custody", "FF JournalCustodySurvivesInterleaving.fieldPolicies",
             "FF JournalCustodySurvivesInterleaving.activeIdentity",
             "FF JournalCustodySurvivesInterleaving.terminalMatrix", "FF JournalCustodySurvivesInterleaving.stickyHeader",
             "FF JournalCustodySurvivesInterleaving.settlementOrder", "FF JournalCustodySurvivesInterleaving.reasonSequence",
             "FF JournalCustodySurvivesInterleaving.timelines", "FF JournalCustodySurvivesInterleaving.phaseSummariesAndIndex",
             "FF JournalCustodySurvivesInterleaving.schedulePolicy", "FF JournalCustodySurvivesInterleaving.lazyPreparation",
             "FF JournalCustodySurvivesInterleaving.preparationBarrier", "FF JournalCustodySurvivesInterleaving.e2ePreparation",
             "FF JournalCustodySurvivesInterleaving.diagnosticWriteFailure", "FF JournalCustodySurvivesInterleaving.authorityBytes",
             "FF JournalCustodySurvivesInterleaving.authorityBinding", "FF JournalCustodySurvivesInterleaving.originalManifestBytes",
             "FF JournalCustodySurvivesInterleaving.artifactSchemas", "FF JournalCustodySurvivesInterleaving.phaseBindings"],
            new ScriptHarnessOptions(TimeSpan.FromSeconds(180), TimeSpan.FromSeconds(10)), cancellationToken);
}
