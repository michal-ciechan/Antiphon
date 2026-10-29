using Antiphon.Tests.TestHelpers;
using TUnit.Core;
using Antiphon.TestSupport;

namespace Antiphon.Tests.Scripts;

[NativeLinuxOnly("CARD-1375 native expiry qualification requires Linux.")]
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class NightlyProbeSupervisorTests
{
    [Test]
    public Task C1375_DeadlineKillsAndJoinsTree()
    {
        NativeLinuxGate.Require("CARD-1375 native expiry qualification requires Linux.");

        return ScriptHarness.RunHarnessCaseAsync("test-nightly-probe-supervisor.ps1", "C1375",
            nameof(C1375_DeadlineKillsAndJoinsTree), 9,
            [
                "C1375 ready identities", "C1375 no early expiry", "C1375 expired",
                "C1375 root exited/joined", "C1375 child exited/joined", "C1375 grandchild exited/joined",
                "C1375 both EOFs", "C1375 shared cleanup elapsed bound", "C1375 no fallback needed"
            ],
            new ScriptHarnessOptions(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(5)),
            CancellationToken.None);
    }
}
