using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReleaseGateFollowupTests
{
    [Test]
    public Task C1336_NightlyCandidateIdentity() =>
        ScriptHarness.RunHarnessCaseAsync("card1336-release-gate-followups.ps1", "C1336",
            "C1336_NightlyCandidateIdentity", 25,
            [
                "C1336 N omitted", "C1336 N whitespace", "C1336 N full-ref",
                "C1336 N repeat", "C1336 N different-pin", "C1336 N explicit",
                "C1336 N foreign", "C1336 N missing-sha", "C1336 N short-sha",
                "C1336 N invalid-sha", "C1336 N bad-ref", "C1336 N legacy",
                "C1336 N sync-selection", "C1336 N nosync-selection",
                "C1336 N hop-child", "C1336 N workload-child", "C1336 N completion",
                "C1336 N rc-green", "C1336 N master-unchanged", "C1336 N master-root",
                "C1336 N noreport", "C1336 N moved-sync", "C1336 N moved-nosync",
                "C1336 N help", "C1336 N no-authority"
            ],
            ScriptHarnessOptions.Default with
            {
                ScriptPath = Path.Combine(DelegateScriptRunner.RepoRoot,
                    "tests", "fixtures", "card1336-release-gate-followups.ps1")
            }, CancellationToken.None);

    [Test]
    public Task C1336_WrapperRef() =>
        ScriptHarness.RunHarnessCaseAsync("card1336-release-gate-followups.ps1", "C1336",
            "C1336_WrapperRef", 9,
            [
                "C1336 W own-0", "C1336 W own-23",
                "C1336 W missing-0", "C1336 W missing-23",
                "C1336 W foreign-0", "C1336 W foreign-23",
                "C1336 W schedule", "C1336 W master-unchanged", "C1336 W documentation"
            ],
            ScriptHarnessOptions.Default with
            {
                ScriptPath = Path.Combine(DelegateScriptRunner.RepoRoot,
                    "tests", "fixtures", "card1336-release-gate-followups.ps1")
            }, CancellationToken.None);
}
