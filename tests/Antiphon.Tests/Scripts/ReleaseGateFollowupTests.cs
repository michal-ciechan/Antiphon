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
            "C1336_NightlyCandidateIdentity", 1,
            ["C1336 N omitted"],
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
