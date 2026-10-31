using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcFailedFirstPlannerTests
{
    // CARD-1404 provisional: one immutable 19,012-node replay per harness, no DB.
    [Test, Timeout(180_000)]
    public Task GlobalReplayPriority(CancellationToken cancellationToken) =>
        ScriptHarness.RunHarnessCaseAsync("test-nightly-failed-first.ps1", "FF", nameof(GlobalReplayPriority), 10,
            ["FF GlobalReplayPriority.replay", "FF GlobalReplayPriority.globalPriority",
             "FF GlobalReplayPriority.stable", "FF GlobalReplayPriority.permutation",
             "FF GlobalReplayPriority.references", "FF GlobalReplayPriority.crossSuite",
             "FF GlobalReplayPriority.allSplits", "FF GlobalReplayPriority.canonicalDigest",
             "FF GlobalReplayPriority.unresolved", "FF GlobalReplayPriority.duplicate"],
            new ScriptHarnessOptions(TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(10)), cancellationToken);
    [Test, Timeout(300_000)]
    public Task FullBytesAndCoverageConserved(CancellationToken cancellationToken) =>
        ScriptHarness.RunHarnessCaseAsync("test-nightly-failed-first.ps1", "FF", nameof(FullBytesAndCoverageConserved), 20,
            ["FF FullBytesAndCoverageConserved.observedPermutation", "FF FullBytesAndCoverageConserved.anchors",
             "FF FullBytesAndCoverageConserved.canonicalBytes", "FF FullBytesAndCoverageConserved.coverage",
             "FF FullBytesAndCoverageConserved.keyedObservations", "FF FullBytesAndCoverageConserved.terminalMatrix",
             "FF FullBytesAndCoverageConserved.prefixState", "FF FullBytesAndCoverageConserved.stickyHeader",
             "FF FullBytesAndCoverageConserved.settlementOrder", "FF FullBytesAndCoverageConserved.reasonSequence",
             "FF FullBytesAndCoverageConserved.journalBindings", "FF FullBytesAndCoverageConserved.timelines",
             "FF FullBytesAndCoverageConserved.phaseSummariesAndIndex", "FF FullBytesAndCoverageConserved.schedulePolicy",
             "FF FullBytesAndCoverageConserved.lazyPreparation", "FF FullBytesAndCoverageConserved.preparationBarrier",
             "FF FullBytesAndCoverageConserved.e2ePreparation", "FF FullBytesAndCoverageConserved.diagnosticWriteFailure",
             "FF FullBytesAndCoverageConserved.authorityBytes", "FF FullBytesAndCoverageConserved.authorityBinding"],
            new ScriptHarnessOptions(TimeSpan.FromSeconds(240), TimeSpan.FromSeconds(10)), cancellationToken);
}
