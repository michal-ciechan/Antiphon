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
        ScriptHarness.RunHarnessCaseAsync("test-nightly-failed-first.ps1", "FF", nameof(ActualLoopFollowsSchedule), 11,
            ["FF ActualLoopFollowsSchedule.actualTrace", "FF ActualLoopFollowsSchedule.permutation",
             "FF ActualLoopFollowsSchedule.oneBuildDiscovery", "FF ActualLoopFollowsSchedule.historyBinding",
             "FF ActualLoopFollowsSchedule.allChunksVerdict", "FF ActualLoopFollowsSchedule.finalProjection",
             "FF ActualLoopFollowsSchedule.fallback", "FF ActualLoopFollowsSchedule.progressOrder",
             "FF ActualLoopFollowsSchedule.heartbeatCollision", "FF ActualLoopFollowsSchedule.heartbeatLegacy",
             "FF ActualLoopFollowsSchedule.heartbeatRefusals"],
            new ScriptHarnessOptions(TimeSpan.FromSeconds(1080), TimeSpan.FromSeconds(10)), cancellationToken);

    [Test, Timeout(180_000)]
    public Task JournalCustodySurvivesInterleaving(CancellationToken cancellationToken) =>
        ScriptHarness.RunHarnessCaseAsync("test-nightly-failed-first.ps1", "FF", nameof(JournalCustodySurvivesInterleaving), 9,
            ["FF JournalCustodySurvivesInterleaving.interleaving", "FF JournalCustodySurvivesInterleaving.hostDiscovery",
             "FF JournalCustodySurvivesInterleaving.prefixState", "FF JournalCustodySurvivesInterleaving.journalBindings",
             "FF JournalCustodySurvivesInterleaving.oneContextClose", "FF JournalCustodySurvivesInterleaving.timeoutPermutation",
             "FF JournalCustodySurvivesInterleaving.custody", "FF JournalCustodySurvivesInterleaving.fieldPolicies",
             "FF JournalCustodySurvivesInterleaving.activeIdentity"],
            new ScriptHarnessOptions(TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(10)), cancellationToken);
}
