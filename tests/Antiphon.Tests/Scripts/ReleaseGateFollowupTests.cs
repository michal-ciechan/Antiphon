using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReleaseGateFollowupTests
{
    [Test]
    public Task C1336_WrapperRef() =>
        ScriptHarness.RunHarnessCaseAsync("card1336-release-gate-followups.ps1", "C1336",
            "C1336_WrapperRef", 1, ["C1336 W own-0"],
            ScriptHarnessOptions.Default with
            {
                ScriptPath = Path.Combine(DelegateScriptRunner.RepoRoot,
                    "tests", "fixtures", "card1336-release-gate-followups.ps1")
            }, CancellationToken.None);
}
