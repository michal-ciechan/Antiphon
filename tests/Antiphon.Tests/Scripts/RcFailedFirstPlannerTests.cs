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
}
